using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

internal static class Q65LiveProbe
{
    public static async Task RunAsync(NamedSignalRing input, string resultsName, string pipe)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = timeout.Token;
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await stream.ConnectAsync(5000, token); return stream; }
                catch { await stream.DisposeAsync(); throw; }
            }
        };
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            { HttpHandler = handler, MaxReceiveMessageSize = 32 * 1024 * 1024 });
        var dsp = new DspWaveform.DspWaveformClient(channel);
        var encoded = await dsp.EncodeQ65Async(new Q65EncodeRequest { Submode = "15A",
            Message = "CQ K1ABC FN42", AudioHz = 1500, Gain = 0.5f }, cancellationToken: ct);
        var configuration = new Q65RxConfiguration { Enabled = true, Submode = "15A", RxHz = 1500, ToleranceHz = 100 };
        var offlineSlot = new byte[encoded.SlotSeconds * 96000];
        encoded.Waveform.Pcm16.Span.CopyTo(offlineSlot.AsSpan(encoded.TxStartSamples12K * 8));
        var offline = await dsp.DecodeOfflineAfAsync(new OfflineAfDecodeRequest { Mode = "Q65", SampleRate = 48000,
            Pcm16 = Google.Protobuf.ByteString.CopyFrom(offlineSlot), Q65 = configuration }, cancellationToken: ct);
        if (!offline.Rows.Any(row => row.Text.Trim() == "CQ K1ABC FN42"))
            throw new InvalidDataException("Independent offline Q65 slot did not decode the encoded message.");
        Console.WriteLine("PASS: deployed DSP offline Q65 independent slot decode.");
        await dsp.ConfigureQ65Async(configuration, cancellationToken: ct);
        using var output = new NamedSignalRing(resultsName, 32, 32768);
        var timeline = new byte[(encoded.SlotSeconds + 2) * 96000];
        encoded.Waveform.Pcm16.Span.CopyTo(timeline.AsSpan(96000 + encoded.TxStartSamples12K * 8));
        input.ResetEpoch(81);
        await FeedAsync(input, timeline, encoded.SlotSeconds, ct);
        await AssertDecodeAsync(output, ct);
        configuration.Enabled = false;
        await dsp.ConfigureQ65Async(configuration, cancellationToken: ct);
        Console.WriteLine("PASS: deployed DSP Q65 native IPC synthesis -> real SignalBus PCM -> UTC receive window -> decoded result.");
    }

    private static async Task FeedAsync(NamedSignalRing input, byte[] timeline, int seconds, CancellationToken ct)
    {
        long now = DateTime.UtcNow.Ticks;
        long period = seconds * TimeSpan.TicksPerSecond;
        long first = (now - DateTime.UnixEpoch.Ticks) / period * period + DateTime.UnixEpoch.Ticks - TimeSpan.TicksPerSecond;
        long stamp = Stopwatch.GetTimestamp();
        for (int index = 0; index < timeline.Length / 2; index += 960)
        {
            double elapsed = (first - now) / (double)TimeSpan.TicksPerSecond + (index + 960) / 48000.0;
            input.Publish((ulong)index, stamp + (long)(elapsed * Stopwatch.Frequency), 48000, 960,
                timeline.AsSpan(index * 2, 1920).ToArray());
            await Task.Delay(12, ct);
        }
    }

    private static async Task AssertDecodeAsync(NamedSignalRing output, CancellationToken ct)
    {
        var cursor = new SignalCursor();
        while (true)
        {
            while (output.TryReadNext(cursor, out var frame) && frame is not null)
            {
                using var doc = JsonDocument.Parse(frame.Payload);
                var root = doc.RootElement;
                if (root.GetProperty("state").GetString() == "error")
                    throw new InvalidDataException(root.GetProperty("error").GetString());
                if (root.TryGetProperty("sourceEpoch", out var epoch) && epoch.GetUInt64() == 81 &&
                    root.GetProperty("messages").EnumerateArray().Any(row =>
                        row.GetProperty("text").GetString()?.Trim() == "CQ K1ABC FN42")) return;
            }
            await Task.Delay(50, ct);
        }
    }
}
