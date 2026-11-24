using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  [CollectionDefinition("SkyCAT scope TCP", DisableParallelization = true)]
  public sealed class SkyCatScopeTcpCollection { }

  [Collection("SkyCAT scope TCP")]
  public sealed class IcomSkyCatScopeClientTests
  {
    [Fact]
    public void AutoControlTargetNeverInventsReceiver()
    {
      IcomScopeControlTarget.TryResolve(
        IcomLanScopeBand.Auto, null,
        IcomScopeReadbackState.ParsePartial("MAIN.MODE=CENTER"),
        out _).Should().BeFalse();

      IcomScopeControlTarget.TryResolve(
        IcomLanScopeBand.Auto, null,
        IcomScopeReadbackState.ParsePartial("SELECT=SUB"),
        out byte selected).Should().BeTrue();
      selected.Should().Be(1);

      IcomScopeControlTarget.TryResolve(
        IcomLanScopeBand.Main, null, null, out selected).Should().BeTrue();
      selected.Should().Be(0);
      IcomScopeControlTarget.TryResolve(
        IcomLanScopeBand.Sub, null, null, out selected).Should().BeTrue();
      selected.Should().Be(1);

      IcomScopeControlTarget.TryResolve(
        IcomLanScopeBand.Auto, new IcomScopeFrame { Scope = 1 },
        null, out selected).Should().BeTrue();
      selected.Should().Be(1);
    }

    [Fact]
    public async Task ScopeOnlyConnectionCanWriteModeAndSpanWithoutPttOrFrequency()
    {
      using var server = new StubSkyCat();
      using var client = new IcomSkyCatScopeClient(server.Port);
      client.Start();

      await WaitForAsync(() => client.IsReady, () => client.LastError ?? "No capability reply");

      client.RequestControl(IcomScopeControlRequest.ForMode(
        0, IcomScopeMode.Fixed)).Should().BeTrue();
      client.RequestControl(IcomScopeControlRequest.ForSpan(
        1, 50_000)).Should().BeTrue();

      await WaitForAsync(
        () => server.Commands.Contains("U SCOPE_MODE MAIN FIXED") &&
              server.Commands.Contains("U SCOPE_SPAN SUB 50000"),
        () => string.Join("; ", server.Commands));

      server.Commands.Should().NotContain(c =>
        c.StartsWith("T ", StringComparison.Ordinal) ||
        c.StartsWith("F ", StringComparison.Ordinal) ||
        c.StartsWith("I ", StringComparison.Ordinal) ||
        c.StartsWith("M ", StringComparison.Ordinal));
      client.RejectedWriteCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingSelectStillProducesPartialScopeReadback()
    {
      using var server = new StubSkyCat { RejectSelect = true };
      using var client = new IcomSkyCatScopeClient(server.Port);
      var completed = new TaskCompletionSource<IcomScopeReadbackState>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      client.ReadbackReceived += state => completed.TrySetResult(state);
      client.Start();

      await WaitForAsync(() => client.IsReady, () => client.LastError ?? "No capability reply");
      client.RequestReadback(restart: true).Should().BeTrue();
      IcomScopeReadbackState readback =
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(8));

      readback.IsPartial.Should().BeTrue();
      readback.HasField("SELECT").Should().BeFalse();
      readback.HasField("MAIN.MODE").Should().BeTrue();
      readback.HasField("MAIN.SPAN").Should().BeTrue();
      readback.MainSpanHz.Should().Be(25_000);
      client.LastReadbackFieldCount.Should().Be(2);
    }

    // Poll asynchronously: the client and server both need ThreadPool
    // continuations. A synchronous SpinUntil on an xUnit worker competes
    // with these operations when the suite is running on a small CI VM.
    private static async Task WaitForAsync(Func<bool> predicate, Func<string> diagnostic)
    {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      while (!predicate() && !timeout.IsCancellationRequested)
      {
        try { await Task.Delay(10, timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { break; }
      }
      predicate().Should().BeTrue("SkyCAT must reply within five seconds; {0}", diagnostic());
    }

    private sealed class StubSkyCat : IDisposable
    {
      private readonly TcpListener listener = new(IPAddress.Loopback, 0);
      private readonly CancellationTokenSource stop = new();
      private readonly Task server;
      private readonly ConcurrentQueue<string> commands = new();

      internal bool RejectSelect { get; set; }
      internal int Port { get; }
      internal string[] Commands => commands.ToArray();

      internal StubSkyCat()
      {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // Accept is asynchronous; start it immediately instead of queuing
        // a second ThreadPool work item before the client can handshake.
        server = RunAsync();
      }

      private async Task RunAsync()
      {
        try
        {
          using var socket = await listener.AcceptTcpClientAsync(stop.Token);
          using var stream = socket.GetStream();
          using var reader = new StreamReader(stream, Encoding.ASCII,
            leaveOpen: true);
          using var writer = new StreamWriter(stream, Encoding.ASCII)
            { AutoFlush = true, NewLine = "\n" };

          while (!stop.IsCancellationRequested)
          {
            string? command = await reader.ReadLineAsync(stop.Token);
            if (command == null) break;
            commands.Enqueue(command);
            string reply = command switch
            {
              "a" => "{\"version\":1,\"model\":\"IC-9700\"}",
              "U SCOPE_READ_FIELD SELECT" when RejectSelect => "RPRT -5",
              "U SCOPE_READ_FIELD SELECT" => "SELECT=MAIN",
              "U SCOPE_READ_FIELD MAIN.MODE" => "MAIN.MODE=CENTER",
              "U SCOPE_READ_FIELD MAIN.SPAN" => "MAIN.SPAN=25000",
              _ when command.StartsWith("U SCOPE_READ_FIELD ",
                StringComparison.Ordinal) => "RPRT -5",
              _ when command.StartsWith("U SCOPE_",
                StringComparison.Ordinal) => "RPRT 0",
              _ => "RPRT -11"
            };
            await writer.WriteLineAsync(reply);
          }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (IOException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
      }

      public void Dispose()
      {
        stop.Cancel();
        listener.Stop();
      }
    }
  }
}
