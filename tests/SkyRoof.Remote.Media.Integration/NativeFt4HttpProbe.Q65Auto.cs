using SkyRoof.RemoteDesktop;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

internal static partial class NativeFt4HttpProbe
{
    private static readonly JsonSerializerOptions Q65WebJson = new(JsonSerializerDefaults.Web);
    private static readonly string[] Q65AutoMessages = ["W1XYZ K1ABC FN42", "W1XYZ K1ABC R-40", "W1XYZ K1ABC 73"];

    private static async Task VerifyQ65AutoAsync(HttpClient anonymous, HttpClient admin, string pipe,
        FakeNativeIcomRadio radio, string accessToken, CancellationToken ct)
    {
        var config = new Q65RxConfiguration
        {
            Enabled = true, Submode = "15A", RxHz = 1500, ToleranceHz = 100,
            MyCall = "K1ABC", HisCall = "W1XYZ"
        };
        var request = new { call = "K1ABC", grid = "FN42", target = "W1XYZ", attempts = 3,
            frequencyHz = 435_550_000L, gain = 0.5f, odd = false, rx = config };
        using var denied = await anonymous.PostAsJsonAsync("/api/v1/dsp/q65/auto", request, ct);
        if (denied.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidDataException("Anonymous automatic Q65 was authorized.");
        var expected = new List<byte[]>();
        foreach (string message in Q65AutoMessages)
        {
            var encoded = await ReadQ65WaveformAsync(pipe, ct, message);
            expected.Add(encoded.Waveform.Pcm16.ToByteArray());
        }

        using var ring = new NamedSignalRing("skyroof-q65-test-results-" + pipe, 32, 32768);
        radio.BeginWaveformCapture();
        using var reader = new DesktopAutoSession(admin.BaseAddress ?? throw new InvalidOperationException("Missing fixture Host"), accessToken);
        await reader.StartAsync("Q65", request, ct);
        await VerifyQ65AutoExchangeAsync(reader, new Q65AutoFixture(expected, radio, ring, config), ct);
    }

    private sealed record Q65AutoFixture(IReadOnlyList<byte[]> Expected, FakeNativeIcomRadio Radio,
        NamedSignalRing Ring, Q65RxConfiguration Config);

    private static async Task VerifyQ65AutoExchangeAsync(DesktopAutoSession reader, Q65AutoFixture fixture, CancellationToken ct)
    {
        string[] replies = ["K1ABC W1XYZ -42", "K1ABC W1XYZ RR73"];
        long previous = -1;
        int messageIndex = 0;
        foreach (string message in Q65AutoMessages)
        {
            var tx = await ReadAutoStateAsync(reader, "transmitting", ct);
            long slot = tx.GetProperty("slotIndex").GetInt64();
            if (slot % 2 != 0 || tx.GetProperty("slotSeconds").GetInt32() != 15 ||
                tx.GetProperty("message").GetString() != message ||
                (previous >= 0 && slot < previous + 4))
                throw new InvalidDataException("Q65 auto message, parity or late-window deferral mismatch.");
            previous = slot;
            await ReadAutoStateAsync(reader, "sent", ct);
            VerifyQ65Pcm(fixture.Radio.EndWaveformCapture(), fixture.Expected[messageIndex]);
            if (messageIndex < replies.Length)
            {
                fixture.Radio.BeginWaveformCapture();
                await PublishQ65ReplyAsync(fixture, slot + 1, replies[messageIndex], ct);
            }
            messageIndex++;
        }
        var completed = await ReadAutoStateAsync(reader, "completed", ct);
        if (completed.GetProperty("reportSent").GetInt32() != -40 ||
            completed.GetProperty("reportReceived").GetInt32() != -42)
            throw new InvalidDataException("Q65 weak-signal reports changed or QSO did not complete.");
        if (await reader.ReadAsync(ct) is not null) throw new InvalidDataException("Unexpected Q65 event after completion.");
        Console.WriteLine("PASS: desktop authenticated native Q65 three-message exchange, complete synthetic result windows, -40 dB report, deferred parity and exact UDP PCM.");
    }

    private static async Task PublishQ65ReplyAsync(Q65AutoFixture fixture, long slot, string message, CancellationToken ct)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(slot * 15);
        var wait = utc.AddSeconds(15.1) - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            state = "decoded", utc, sourceEpoch = 321UL, config = fixture.Config,
            messages = new[] { new { text = message, snrDb = -40.0 } }
        }, Q65WebJson);
        fixture.Ring.Publish(0, Stopwatch.GetTimestamp(), 48000, 1, bytes);
    }
}
