using SkyRoof.RemoteDesktop;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SkyRoof.Remote.SignalBus;

internal static partial class NativeFt4HttpProbe
{
    private static readonly string[] AutoMessages = ["W1XYZ K1ABC FN42", "W1XYZ K1ABC R-15", "W1XYZ K1ABC 73"];

    private static async Task VerifyAutoAsync(HttpClient anonymous, HttpClient admin,
        string dspPipe, FakeNativeIcomRadio radio, string accessToken, CancellationToken ct)
    {
        var request = new { call = "K1ABC", grid = "FN42", target = "W1XYZ", attempts = 3,
            frequencyHz = 435_550_000L, audioHz = 1500, gain = 0.5f, odd = false };
        using var denied = await anonymous.PostAsJsonAsync("/api/v1/dsp/ft4/auto", request, ct);
        if (denied.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Anonymous automatic FT4 was not denied.");

        using var ring = new NamedSignalRing("skyroof-ft4-test-results-" + dspPipe, 32, 32768);
        var expected = new List<byte[]>();
        foreach (string message in AutoMessages)
            expected.Add(await ReadWaveformAsync(dspPipe, ct, message));
        radio.BeginWaveformCapture();
        using var reader = new DesktopAutoSession(admin.BaseAddress ?? throw new InvalidOperationException("Missing fixture Host"), accessToken);
        await reader.StationAsync(ct);
        await reader.StartAsync("FT4", request, ct);
        await VerifyAutoExchangeAsync(reader, new AutoFixture(expected, radio, ring), ct);
    }

    private sealed record AutoFixture(IReadOnlyList<byte[]> Expected, FakeNativeIcomRadio Radio, NamedSignalRing Ring);

    private static async Task VerifyAutoExchangeAsync(DesktopAutoSession reader, AutoFixture fixture, CancellationToken ct)
    {
        string[] replies = ["K1ABC W1XYZ -09", "K1ABC W1XYZ RR73"];
        long previousSlot = -1;
        for (int i = 0; i < AutoMessages.Length; i++)
        {
            var transmitting = await ReadAutoStateAsync(reader, "transmitting", ct);
            long slot = transmitting.GetProperty("slotIndex").GetInt64();
            if (transmitting.GetProperty("message").GetString() != AutoMessages[i] ||
                (previousSlot >= 0 && slot != previousSlot + 2))
                throw new InvalidDataException("FT4 sequencer chose the wrong message or UTC slot.");
            previousSlot = slot;
            await ReadAutoStateAsync(reader, "sent", ct);
            VerifyAutoPcm(fixture.Radio.EndWaveformCapture(), fixture.Expected[i]);
            if (i < replies.Length)
            {
                fixture.Radio.BeginWaveformCapture();
                await PublishAutoReplyAsync(fixture.Ring, slot + 1, replies[i], ct);
            }
        }
        var completed = await ReadAutoStateAsync(reader, "completed", ct);
        if (completed.GetProperty("peer").GetString() != "W1XYZ" ||
            completed.GetProperty("reportSent").GetInt32() != -15 ||
            completed.GetProperty("reportReceived").GetInt32() != -9)
            throw new InvalidDataException("FT4 completed without the expected peer/reports.");
        Console.WriteLine("PASS: authenticated automatic FT4 three-slot QSO, synthetic decoder windows, exact native UDP PCM and final receipt.");
    }

    private static async Task PublishAutoReplyAsync(NamedSignalRing ring, long slot, string message, CancellationToken ct)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(slot * 7500);
        // Inject the decoded-result fixture in the live RX window, not before it starts.
        // The separately tested native DSP is not the producer of these synthetic replies.
        var wait = utc.AddSeconds(5.5) - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            utc, sourceEpoch = 123UL, messages = new[] { "000001 -15  0.0 1500 +  " + message }
        });
        ring.Publish(0, Stopwatch.GetTimestamp(), 48000, 1, payload);
    }

    private static async Task<JsonElement> ReadAutoStateAsync(DesktopAutoSession reader, string state, CancellationToken ct)
    {
        while (await reader.ReadAsync(ct) is { } root)
        {
            string? current = root.GetProperty("state").GetString();
            if (current == "error" || current == "attempts-exhausted" || current == "rx-window-missed")
                throw new InvalidDataException("Automatic digital QSO interrupted: " + root.GetRawText());
            if (current == state) return root.Clone();
        }
        throw new EndOfStreamException("Missing automatic FT4 state: " + state);
    }

    private static void VerifyAutoPcm(short[] captured, byte[] expected)
    {
        if (captured.Length * 2 != expected.Length)
            throw new InvalidDataException("Automatic FT4 sample count mismatch.");
        for (int i = 0; i < captured.Length; i++)
            if (captured[i] != BinaryPrimitives.ReadInt16LittleEndian(expected.AsSpan(i * 2)))
                throw new InvalidDataException("Automatic FT4 UDP PCM differs from native encoding.");
    }
}
