using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomMicrowaveVirtualPttTests
  {
    private static bool Until(Func<bool> predicate, int timeoutMs = 7000) =>
      SpinWait.SpinUntil(predicate, TimeSpan.FromMilliseconds(timeoutMs));

    [Fact]
    public void Ic905PttOnRetriesRejectedFrequencyBeforeKeying()
    {
      using var rig = new SimulatedIcomRig { RejectFirstFrequencyWrite = true };
      using var cat = Start(rig);
      cat.SetTxFrequency(10_368_100_009.8);
      cat.SetTxMode(Slicer.Mode.USB);
      cat.SetPtt(true);

      Assert.True(Until(() => rig.OnCommands > 0),
        $"PTT ON did not follow CAT retry: {rig.Commands}; {rig.LastError}");
      Assert.False(rig.KeyedBeforeTuned);
      Assert.True(rig.FrequencyWrites >= 2);
      Assert.Equal("10368100010", rig.LastAcceptedFrequency);
      cat.SetPtt(false);
      Assert.True(Until(() => rig.OffCommands > 0));
    }

    [Fact]
    public void QueuedPttOffBypassesSubsequentRejectedFrequencyWrites()
    {
      using var rig = new SimulatedIcomRig();
      using var cat = Start(rig);
      cat.SetTxFrequency(5_760_100_000);
      cat.SetTxMode(Slicer.Mode.USB);
      cat.SetPtt(true);
      Assert.True(Until(() => rig.OnCommands > 0));
      rig.RejectAllFutureFrequencyWrites = true;
      cat.SetTxFrequency(5_760_105_500);
      cat.SetPtt(false);
      Assert.True(Until(() => rig.OffCommands > 0 && !rig.IsKeyed),
        $"PTT OFF was blocked by CAT tune failure: {rig.Commands}");
    }

    [Fact]
    public void TenGigahertzDopplerMayUpdateTxWhileKeyedAndReturnToRx()
    {
      using var rig = new SimulatedIcomRig();
      using var cat = Start(rig);
      var link = new RadioLink
      {
        IsTerrestrial = false,
        Tx = new SatnogsDbTransmitter
        {
          uplink_low = 10_368_100_000
        },
        SatCust = new SatelliteCustomization(),
        TxCust = new TransmitterCustomization(),
        DopplerFactor = 7.2 / RadioLink.SpeedOfLightKmPerSecond
      };
      link.UplinkDopplerCorrectionEnabled = true;
      link.UplinkManualCorrectionEnabled = false;

      link.ComputeFrequencies();
      cat.SetTxFrequency(link.CorrectedUplinkFrequency);
      cat.SetTxMode(Slicer.Mode.USB);
      cat.SetPtt(true);
      Assert.True(Until(() => rig.OnCommands > 0),
        $"Initial microwave key never completed: {rig.Commands}");

      link.DopplerFactor = 7.5 / RadioLink.SpeedOfLightKmPerSecond;
      link.ComputeFrequencies();
      string expected = CatControlEngine.QuantizeCatFrequency(
        link.CorrectedUplinkFrequency, 10, 1).ToString();
      cat.SetTxFrequency(link.CorrectedUplinkFrequency);
      Assert.True(Until(() => rig.LastAcceptedFrequency == expected),
        $"Transmitting 10 GHz Doppler CAT update was lost: {rig.Commands}");

      cat.SetPtt(false);
      Assert.True(Until(() => rig.OffCommands > 0 && !rig.IsKeyed),
        $"Stop after frequency update failed: {rig.Commands}");
      Assert.False(rig.KeyedBeforeTuned);
    }

    private static CatControlEngine Start(SimulatedIcomRig rig)
    {
      var previous = SynchronizationContext.Current;
      SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
      try
      {
        var cat = new CatControlEngine(
          new CatRadioSettings { Host = "127.0.0.1", Port = rig.Port },
          new CatSettings
          {
            Delay = 50, TuningStep = 10, MicrowaveTuningStep = 1,
            SendTimeout = 1200, ReceiveTimeout = 1200, ReconnectDelay = 100
          });
        cat.Start(rx: false, tx: true, crossband: false);
        Assert.True(Until(() => cat.IsRunning),
          $"Virtual IC-905 CAT did not connect: {rig.Commands}");
        return cat;
      }
      finally
      {
        SynchronizationContext.SetSynchronizationContext(previous);
      }
    }

    private sealed class SimulatedIcomRig : IDisposable
    {
      private const string Capabilities =
        "{\"version\":1,\"model\":\"IC-905\",\"cross_band_split\":false," +
        "\"simplex\":{\"when_receiving\":[\"read_ptt\",\"read_tx_frequency\",\"write_rx_frequency\",\"write_rx_mode\",\"write_ptt_on\",\"write_ptt_off\"]," +
        "\"when_transmitting\":[\"read_ptt\",\"read_tx_frequency\",\"write_tx_frequency\",\"write_tx_mode\",\"write_ptt_on\",\"write_ptt_off\"]," +
        "\"when_setting_up\":[\"write_rx_frequency\",\"write_rx_mode\"]}}";

      private readonly TcpListener listener;
      private readonly CancellationTokenSource cancellation = new();
      private readonly Task server;
      private readonly ConcurrentQueue<string> commands = new();
      private int keyed;
      private int tuned;
      private int modeReady;
      private int frequencyWrites;
      private int onCommands;
      private int offCommands;
      private int rejectedFirst;
      private int keyedBeforeTuned;

      internal bool RejectFirstFrequencyWrite { get; set; }
      internal bool RejectAllFutureFrequencyWrites { get; set; }
      internal bool KeyedBeforeTuned => Volatile.Read(ref keyedBeforeTuned) != 0;
      internal bool IsKeyed => Volatile.Read(ref keyed) != 0;
      internal int FrequencyWrites => Volatile.Read(ref frequencyWrites);
      internal int OnCommands => Volatile.Read(ref onCommands);
      internal int OffCommands => Volatile.Read(ref offCommands);
      internal string? LastAcceptedFrequency { get; private set; }
      internal string? LastError { get; private set; }
      internal string Commands => string.Join(" | ", commands);
      internal ushort Port { get; }

      internal SimulatedIcomRig()
      {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
        server = Task.Run(RunAsync);
      }

      private async Task RunAsync()
      {
        try
        {
          while (!cancellation.IsCancellationRequested)
          {
            using var client = await listener.AcceptTcpClientAsync(
              cancellation.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(
              stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(
              stream, Encoding.ASCII, leaveOpen: true)
            { AutoFlush = true, NewLine = "\n" };
            while (!cancellation.IsCancellationRequested)
            {
              string? line = await reader.ReadLineAsync(cancellation.Token);
              if (line == null) break;
              commands.Enqueue(line);
              string reply = line switch
              {
                "a" => Capabilities,
                "t" => IsKeyed ? "1" : "0",
                "f" or "i" => LastAcceptedFrequency ?? "10368100000",
                "T 1" => KeyOn(),
                "T 0" => KeyOff(),
                _ when line.StartsWith("F ", StringComparison.Ordinal) =>
                  WriteFrequency(line[2..]),
                _ when line.StartsWith("I ", StringComparison.Ordinal) =>
                  WriteFrequency(line[2..]),
                _ when line.StartsWith("M ", StringComparison.Ordinal) =>
                  WriteMode(),
                _ => "RPRT 0"
              };
              await writer.WriteLineAsync(reply);
            }
          }
        }
        catch (OperationCanceledException) when(cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex) when(cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
          LastError = ex.ToString();
        }
      }

      private string WriteFrequency(string hz)
      {
        Interlocked.Increment(ref frequencyWrites);
        if (RejectAllFutureFrequencyWrites ||
            (RejectFirstFrequencyWrite &&
             Interlocked.Exchange(ref rejectedFirst, 1) == 0))
          return "RPRT -1";

        LastAcceptedFrequency = hz;
        Interlocked.Exchange(ref tuned, 1);
        return "RPRT 0";
      }

      private string WriteMode()
      {
        Interlocked.Exchange(ref modeReady, 1);
        return "RPRT 0";
      }

      private string KeyOn()
      {
        if (Volatile.Read(ref tuned) == 0 ||
            Volatile.Read(ref modeReady) == 0)
          Interlocked.Exchange(ref keyedBeforeTuned, 1);
        Interlocked.Exchange(ref keyed, 1);
        Interlocked.Increment(ref onCommands);
        return "RPRT 0";
      }

      private string KeyOff()
      {
        Interlocked.Exchange(ref keyed, 0);
        Interlocked.Increment(ref offCommands);
        return "RPRT 0";
      }

      public void Dispose()
      {
        cancellation.Cancel();
        listener.Stop();
        try { server.Wait(TimeSpan.FromSeconds(2)); } catch { }
        cancellation.Dispose();
      }
    }
  }
}
