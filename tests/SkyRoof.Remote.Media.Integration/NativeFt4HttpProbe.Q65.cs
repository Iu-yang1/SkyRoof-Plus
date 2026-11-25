using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

internal static partial class NativeFt4HttpProbe
{
    private static async Task VerifyQ65Async(HttpClient anonymous, HttpClient admin, string pipe,
        FakeNativeIcomRadio radio, CancellationToken ct)
    {
        var request = new { submode = "15A", message = "CQ K1ABC FN42", frequencyHz = 435_550_000L,
            audioHz = 1500, gain = 0.5f, odd = false };
        using var denied = await anonymous.PostAsJsonAsync("/api/v1/dsp/q65/transmit", request, ct);
        if (denied.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidDataException("Anonymous Q65 TX was authorized.");
        var expected = await ReadQ65WaveformAsync(pipe, ct);
        radio.BeginWaveformCapture();
        using var response = await admin.PostAsJsonAsync("/api/v1/dsp/q65/transmit", request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidDataException("Q65 transmit: " + body);
        using var receipt = JsonDocument.Parse(body);
        var result = receipt.RootElement;
        if (result.GetProperty("state").GetString() != "completed" ||
            result.GetProperty("sourceSamples").GetInt64() != expected.Waveform.Pcm16.Length / 2)
            throw new InvalidDataException("Q65 timed TX receipt incorrect.");
        var target = result.GetProperty("targetUtc").GetDateTimeOffset();
        long period = expected.SlotSeconds * TimeSpan.TicksPerSecond;
        long offset = expected.TxStartSamples12K * TimeSpan.TicksPerSecond / 12000;
        if ((target.UtcTicks - DateTime.UnixEpoch.Ticks - offset) % (period * 2) != 0)
            throw new InvalidDataException("Q65 even UTC slot or native start offset incorrect.");
        VerifyQ65Pcm(radio.EndWaveformCapture(), expected.Waveform.Pcm16.ToByteArray());
        Console.WriteLine("PASS: authenticated native Q65 15A timed TX, correct UTC parity and exact UDP PCM with final packet padding.");
    }

    private static void VerifyQ65Pcm(short[] captured, byte[] expected)
    {
        int count = expected.Length / 2;
        if (captured.Length != (count + 959) / 960 * 960)
            throw new InvalidDataException("Q65 PCM packet geometry incorrect.");
        for (int i = 0; i < count; i++)
            if (captured[i] != BinaryPrimitives.ReadInt16LittleEndian(expected.AsSpan(i * 2, 2)))
                throw new InvalidDataException("Q65 UDP PCM differs from native encoding.");
        if (captured.Skip(count).Any(value => value != 0))
            throw new InvalidDataException("Q65 final packet padding is not silence.");
    }

    private static async Task<Q65EncodedWaveform> ReadQ65WaveformAsync(string pipe, CancellationToken ct, string message = "CQ K1ABC FN42")
    {
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
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        return await new DspWaveform.DspWaveformClient(channel).EncodeQ65Async(new Q65EncodeRequest
        {
            Submode = "15A", Message = message, AudioHz = 1500, Gain = 0.5f
        }, cancellationToken: ct);
    }
}
