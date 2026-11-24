using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

internal static partial class CwLiveProbe
{
    public static async Task RunAsync(NamedSignalRing input, string resultsName, string pipe)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var ct = timeout.Token;
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await client.ConnectAsync(5000, token); return client; }
                catch { await client.DisposeAsync(); throw; }
            }
        };
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        var control = new DspWaveform.DspWaveformClient(channel);
        using var output = new NamedSignalRing(resultsName, 16, 32768);
        await control.ConfigureCwAsync(new CwRxConfiguration { Enabled = true, Mode = "NewCW" },
            cancellationToken: ct);
        input.ResetEpoch(71);
        var cursor = new SignalCursor();
        byte[] pcm = MakeWaveform(12);
        await FeedAsync(input, pcm, ct);
        await WaitForTracksAndDecodeAsync(output, cursor, ct);

        ulong oldEpoch = output.StreamEpoch;
        input.ResetEpoch(72);
        input.Publish(0, Stopwatch.GetTimestamp(), 48000, 960, new byte[1920]);
        await WaitForResetAsync(output, oldEpoch, 72, ct);
        await control.ConfigureCwAsync(new CwRxConfiguration { Enabled = false, Mode = "NewCW" },
            cancellationToken: ct);
        await WaitForStateAsync(output, state => state.GetProperty("state").GetString() == "disabled", ct);
        Console.WriteLine("PASS: deployed CW DSP receives real SignalBus PCM, tracks two carriers, runs NewCW decode and clears the previous epoch.");
    }

    private static async Task FeedAsync(NamedSignalRing input, byte[] pcm, CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < pcm.Length / 2; index += 960)
        {
            // Pace against monotonic elapsed time, not one timer drift per packet.
            var due = TimeSpan.FromSeconds((index + 960) / 48000.0);
            var delay = due - Stopwatch.GetElapsedTime(start);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            input.Publish((ulong)index, Stopwatch.GetTimestamp(), 48000, 960,
                pcm.AsSpan(index * 2, 1920).ToArray());
        }
    }

    private static async Task WaitForTracksAndDecodeAsync(NamedSignalRing output,
        SignalCursor cursor, CancellationToken ct)
    {
        while (true)
        {
            while (output.TryReadNext(cursor, out var frame) && frame is not null)
            {
                using var doc = JsonDocument.Parse(frame.Payload);
                var state = doc.RootElement;
                if (state.GetProperty("sourceEpoch").GetUInt64() != 71 ||
                    !state.TryGetProperty("completedInferenceWindows", out var completed) ||
                    completed.GetInt64() == 0) continue;
                var tracks = state.GetProperty("tracks").EnumerateArray().ToArray();
                if (HasTrack(tracks, 800) && HasTrack(tracks, 1200))
                {
                    Console.WriteLine("CW actual state: " + state);
                    return;
                }
            }
            await Task.Delay(50, ct);
        }
    }

    private static bool HasTrack(JsonElement[] tracks, double frequency) =>
        tracks.Any(track => track.GetProperty("confirmed").GetBoolean() &&
            Math.Abs(track.GetProperty("frequencyHz").GetDouble() - frequency) < 25);

    private static Task WaitForResetAsync(NamedSignalRing output, ulong oldEpoch,
        ulong sourceEpoch, CancellationToken ct) =>
        WaitForStateAsync(output, state => output.StreamEpoch != oldEpoch &&
            state.GetProperty("sourceEpoch").GetUInt64() == sourceEpoch &&
            state.GetProperty("transcripts").GetArrayLength() == 0, ct);

    private static async Task WaitForStateAsync(NamedSignalRing output, Func<JsonElement, bool> match,
        CancellationToken ct)
    {
        var cursor = new SignalCursor();
        while (true)
        {
            while (output.TryReadNext(cursor, out var frame) && frame is not null)
            {
                using var doc = JsonDocument.Parse(frame.Payload);
                if (match(doc.RootElement)) return;
            }
            await Task.Delay(50, ct);
        }
    }
}
