using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Serilog;

namespace SkyRoof
{
  /// <summary>
  /// Scope-only CAT connection for operators who use SkyCAT's waveform TCP
  /// stream but do not route SkyRoof's satellite RX/TX CAT through SkyCAT.
  /// It never issues frequency, mode, or PTT commands; every scope transaction
  /// goes through skycatd's existing serial command lock.
  /// </summary>
  internal sealed class IcomSkyCatScopeClient : IDisposable
  {
    private enum WorkKind { Write, Readback, FixedEdge }
    private sealed record Work(WorkKind Kind, string Command = "",
      int Range = 0, int Edge = 0, int Generation = 0);

    private static readonly string[] ReadbackKeys =
    [
      "SELECT", "MAIN.MODE", "MAIN.SPAN", "MAIN.EDGE",
      "MAIN.REF", "MAIN.SPEED", "MAIN.VBW",
      "SUB.MODE", "SUB.SPAN", "SUB.EDGE",
      "SUB.REF", "SUB.SPEED", "SUB.VBW",
      "TX", "CENTER", "MARKER"
    ];

    private readonly int Port;
    private readonly Channel<Work> Jobs =
      Channel.CreateBounded<Work>(new BoundedChannelOptions(64)
      {
        SingleReader = true, SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
      });
    private readonly CancellationTokenSource StopToken = new();
    private Task? Worker;
    private TcpClient? Socket;
    private StreamReader? Reader;
    private StreamWriter? Writer;
    private int ReadyValue;
    private int SyncGeneration;
    private int SyncQueued;
    private int LastFieldCount = -1;
    private long RejectedWrites;
    private string? LastFailure;

    internal bool IsReady => Volatile.Read(ref ReadyValue) != 0;
    internal int LastReadbackFieldCount => Volatile.Read(ref LastFieldCount);
    internal long RejectedWriteCount => Interlocked.Read(ref RejectedWrites);
    internal string? LastError => LastFailure;
    internal int PortNumber => Port;

    internal event Action<IcomScopeReadbackState>? ReadbackReceived;
    internal event Action<IcomFixedEdgeReadbackState>? FixedEdgeReceived;

    internal IcomSkyCatScopeClient(int port)
    {
      if (port is < 1 or > 65535)
        throw new ArgumentOutOfRangeException(nameof(port));
      Port = port;
    }

    internal void Start()
    {
      Worker ??= Task.Run(() => RunAsync(StopToken.Token));
    }

    internal bool RequestControl(IcomScopeControlRequest request)
    {
      if (!IsReady) return false;
      string command = CatControl.FormatIcomScopeCommand(request);
      Interlocked.Increment(ref SyncGeneration);
      return Jobs.Writer.TryWrite(new Work(WorkKind.Write, command));
    }

    internal bool RequestScopeOutput()
    {
      if (!IsReady) return false;
      return Jobs.Writer.TryWrite(new Work(WorkKind.Write, "U SCOPE 1")) &&
        Jobs.Writer.TryWrite(new Work(WorkKind.Write, "U SCOPE_DATA 1"));
    }

    internal bool RequestReadback(bool restart = false)
    {
      if (!IsReady) return false;
      int generation = restart
        ? Interlocked.Increment(ref SyncGeneration)
        : Volatile.Read(ref SyncGeneration);
      if (!restart && Interlocked.Exchange(ref SyncQueued, 1) != 0)
        return true;
      if (Jobs.Writer.TryWrite(new Work(WorkKind.Readback, Generation: generation)))
        return true;
      Interlocked.Exchange(ref SyncQueued, 0);
      return false;
    }

    internal bool RequestFixedEdgeReadback(int range, int edge)
    {
      if (!IsReady || range is < 1 or > 3 || edge is < 1 or > 4)
        return false;
      return Jobs.Writer.TryWrite(
        new Work(WorkKind.FixedEdge, Range: range, Edge: edge));
    }

    private async Task RunAsync(CancellationToken token)
    {
      while (!token.IsCancellationRequested)
      {
        try
        {
          using var socket = new TcpClient();
          Socket = socket;
          await socket.ConnectAsync(IPAddress.Loopback, Port, token)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(2), token);
          socket.NoDelay = true;
          using NetworkStream stream = socket.GetStream();
          using var reader = new StreamReader(stream, Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false, bufferSize: 1024,
            leaveOpen: true);
          using var writer = new StreamWriter(stream, Encoding.ASCII,
            bufferSize: 1024, leaveOpen: true)
            { AutoFlush = true, NewLine = "\n" };
          Reader = reader;
          Writer = writer;

          string? capabilities = await SendAsync("a", token);
          if (capabilities == null ||
              !capabilities.Contains("IC-9700", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
              $"TCP/{Port} is not an IC-9700 SkyCAT control server.");

          LastFailure = null;
          Volatile.Write(ref ReadyValue, 1);
          while (!token.IsCancellationRequested)
          {
            bool hasWork;
            try
            {
              hasWork = await Jobs.Reader.WaitToReadAsync(token).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), token);
            }
            catch (TimeoutException)
            {
              // The scope TCP stream can keep running after the separate CAT
              // socket goes stale. Check the already-supported static "a"
              // capability reply without touching radio frequency/PTT.
              string? health = await SendAsync("a", token);
              if (health == null ||
                  !health.Contains("IC-9700", StringComparison.OrdinalIgnoreCase))
                throw new IOException("SkyCAT CAT capability heartbeat failed.");
              continue;
            }
            if (!hasWork) break;
            while (Jobs.Reader.TryRead(out Work? work))
              await ExecuteAsync(work, token);
          }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
          break;
        }
        catch (Exception ex)
        {
          if (!string.Equals(LastFailure, ex.Message, StringComparison.Ordinal))
            Log.Warning(ex, "SkyCAT independent scope CAT TCP/{Port} disconnected", Port);
          LastFailure = ex.Message;
        }
        finally
        {
          Volatile.Write(ref ReadyValue, 0);
          Reader = null;
          Writer = null;
          Socket?.Dispose();
          Socket = null;
          while (Jobs.Reader.TryRead(out _)) { }
          Interlocked.Exchange(ref SyncQueued, 0);
          Volatile.Write(ref LastFieldCount, -1);
        }

        try { await Task.Delay(TimeSpan.FromSeconds(2), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
      }
    }

    private async Task ExecuteAsync(Work work, CancellationToken token)
    {
      if (work.Kind == WorkKind.Readback)
      {
        Interlocked.Exchange(ref SyncQueued, 0);
        await ReadScopeAsync(work.Generation, token);
        return;
      }
      if (work.Kind == WorkKind.FixedEdge)
      {
        string? edge = await SendAsync(
          $"U SCOPE_READ_EDGE {work.Range} {work.Edge}", token);
        if (edge != null && !edge.StartsWith("RPRT ", StringComparison.Ordinal))
          FixedEdgeReceived?.Invoke(IcomFixedEdgeReadbackState.Parse(edge));
        return;
      }

      string? response = await SendAsync(work.Command, token);
      if (response != "RPRT 0")
      {
        Interlocked.Increment(ref RejectedWrites);
        LastFailure = $"{work.Command}: {response ?? "<no reply>"}";
        Log.Warning("SkyCAT scope command rejected: {Error}", LastFailure);
      }
      else if (work.Command.StartsWith("U SCOPE_", StringComparison.Ordinal))
      {
        RequestReadback();
      }
    }

    private async Task ReadScopeAsync(int generation, CancellationToken token)
    {
      var fields = new List<string>(ReadbackKeys.Length);
      foreach (string key in ReadbackKeys)
      {
        if (generation != Volatile.Read(ref SyncGeneration))
          return;
        string? response = await SendAsync($"U SCOPE_READ_FIELD {key}", token);
        if (response == null ||
            !response.StartsWith(key + "=", StringComparison.Ordinal))
          continue;
        try
        {
          _ = IcomScopeReadbackState.ParsePartial(response);
          fields.Add(response);
        }
        catch (FormatException ex)
        {
          Log.Warning(ex, "SkyCAT scope field {Key} invalid: {Reply}", key, response);
        }
      }

      if (generation != Volatile.Read(ref SyncGeneration)) return;
      Volatile.Write(ref LastFieldCount, fields.Count);
      if (fields.Count == 0)
      {
        LastFailure = "SkyCAT scope register readback: 0/16 valid fields";
        return;
      }

      var state = IcomScopeReadbackState.ParsePartial(string.Join(";", fields));
      ReadbackReceived?.Invoke(state);
    }

    private async Task<string?> SendAsync(string command, CancellationToken token)
    {
      StreamWriter? writer = Writer;
      StreamReader? reader = Reader;
      if (writer == null || reader == null)
        throw new IOException("SkyCAT scope TCP session is not connected.");

      await writer.WriteLineAsync(command.AsMemory(), token);
      string? reply = await reader.ReadLineAsync(token).AsTask()
        .WaitAsync(TimeSpan.FromMilliseconds(1300), token);
      return reply ?? throw new IOException(
        "SkyCAT scope TCP server closed its command connection.");
    }

    public void Dispose()
    {
      StopToken.Cancel();
      Jobs.Writer.TryComplete();
      Socket?.Dispose();
      // Worker observes cancellation and completes independently; the WinForms
      // UI must not block on a TCP disconnect or CI-V register timeout.
    }
  }
}
