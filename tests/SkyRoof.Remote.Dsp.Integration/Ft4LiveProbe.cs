using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;
using System.Text.Json;
using SkyRoof.Remote.SignalBus;

internal static class Ft4LiveProbe
{
    public static async Task RunAsync(string dspDll, NamedSignalRing input, string resultsName, string pipe)
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pcm");
        try
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            foreach (string arg in new[] { dspDll, "--ft4-encode", "CQ K1ABC FN42", file })
                info.ArgumentList.Add(arg);
            using var encoder = Process.Start(info) ?? throw new InvalidOperationException("FT4 encoder unavailable.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await encoder.WaitForExitAsync(timeout.Token);
            if (encoder.ExitCode != 0) throw new InvalidDataException("FT4 waveform generation failed.");
            byte[] waveform = await File.ReadAllBytesAsync(file, timeout.Token);
            await AssertOfflineAsync(waveform, pipe, timeout.Token);
            byte[] timeline = new byte[408000 * 2];
            // Start 1 second before a slot. Place FT4 at slot + 0.5 seconds.
            waveform.CopyTo(timeline, 72000 * 2);
            using var output = new NamedSignalRing(resultsName, 32, 32768);
            var cursor = new SignalCursor();
            input.ResetEpoch(13);
            await PublishAsync(input, timeline, timeout.Token);
            await AssertResultAsync(output, cursor, timeout.Token);
        }
        finally { File.Delete(file); }
    }

    private static async Task AssertOfflineAsync(byte[] waveform, string pipe, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler { ConnectCallback = async (_, token) =>
        {
            var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.None);
            try { await stream.ConnectAsync(5000, token); return stream; }
            catch { await stream.DisposeAsync(); throw; }
        }};
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        var dsp = new DspWaveform.DspWaveformClient(channel);
        var slot = new byte[290304 * 2];
        waveform.CopyTo(slot, 24000 * 2);
        var result = await dsp.DecodeOfflineAfAsync(new OfflineAfDecodeRequest { Mode = "FT4", SampleRate = 48000,
            Pcm16 = Google.Protobuf.ByteString.CopyFrom(slot) }, cancellationToken: ct);
        if (!result.Messages.Any(m => m.Contains("K1ABC", StringComparison.Ordinal)))
            throw new InvalidDataException("Independent offline FT4 slot did not decode the encoded message.");
        Console.WriteLine("PASS: deployed DSP offline FT4 independent slot decode.");
    }

    private static async Task PublishAsync(NamedSignalRing input, byte[] timeline, CancellationToken ct)
    {
        const long slotTicks = TimeSpan.TicksPerSecond * 15 / 2;
        long now = DateTime.UtcNow.Ticks;
        long first = now / slotTicks * slotTicks - TimeSpan.TicksPerSecond;
        long stamp = Stopwatch.GetTimestamp();
        for (int index = 0; index < timeline.Length / 2; index += 960)
        {
            double seconds = (first - now) / (double)TimeSpan.TicksPerSecond + (index + 960) / 48000.0;
            long publish = stamp + (long)(seconds * Stopwatch.Frequency);
            input.Publish((ulong)index, publish, 48000, 960, timeline.AsSpan(index * 2, 1920).ToArray());
            await Task.Delay(10, ct);
        }
    }

    private static async Task AssertResultAsync(NamedSignalRing output, SignalCursor cursor, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            while (output.TryReadNext(cursor, out var frame) && frame is not null)
            {
                using var document = JsonDocument.Parse(frame.Payload);
                var result = document.RootElement;
                if (result.GetProperty("sourceEpoch").GetUInt64() != 13) continue;
                if (result.GetProperty("messages").EnumerateArray().Any(m =>
                    m.GetString()?.Contains("K1ABC", StringComparison.Ordinal) == true))
                {
                    Console.WriteLine("PASS: deployed DSP SignalBus -> UTC FT4 window -> native decode -> result ring.");
                    return;
                }
            }
            await Task.Delay(50, ct);
        }
        throw new TimeoutException("Live FT4 result missing.");
    }
}
