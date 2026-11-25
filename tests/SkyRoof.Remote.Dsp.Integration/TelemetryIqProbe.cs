using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

internal static class TelemetryIqProbe
{
    public static async Task RunAsync(string resultsName, string pipe, string corpus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        var dsp = new DspWaveform.DspWaveformClient(channel);
        using var output = new NamedSignalRing(resultsName, 64, 262144);
        await VerifyAsync(dsp, output, Path.Combine(corpus, "pass_t211s_type01.iq"), -5289, 1, 192304, ct);
        await VerifyAsync(dsp, output, Path.Combine(corpus, "pass_t032s_type14.iq"), -3087, 14, 192124, ct);
    }

    private static async Task VerifyAsync(DspWaveform.DspWaveformClient dsp, NamedSignalRing output,
        string path, double center, int type, uint clock, CancellationToken ct)
    {
        var config = new TelemetryRxConfiguration
        {
            Enabled = true, NoradId = 101101, Satellite = "UNNE-1B",
            DecoderProtocol = "unne1b", Modulation = "FSK", Framing = "HADES",
            Baud = 200, DeviationHz = 820, Input = "iq-cf32"
        };
        await dsp.ConfigureTelemetryAsync(config, cancellationToken: ct);
        using var call = dsp.PublishTelemetryIq(cancellationToken: ct);
        ulong index = 0;
        async Task SendAsync(byte[] data)
        {
            await call.RequestStream.WriteAsync(new TelemetryIqBlock
            {
                FirstSampleIndex = index, SampleRate = 48000, Cf32 = ByteString.CopyFrom(data)
            }, ct);
            index += (ulong)(data.Length / 8);
        }
        var silence = new byte[4096 * 8];
        for (int i = 0; i < 5; i++) await SendAsync(silence);
        await SendAsync(new byte[(24000 - 5 * 4096) * 8]);
        byte[] tuned = Resample(await File.ReadAllBytesAsync(path, ct), center + 800);
        for (int offset = 0; offset < tuned.Length; offset += silence.Length)
            await SendAsync(tuned.AsSpan(offset, Math.Min(silence.Length, tuned.Length - offset)).ToArray());
        for (int i = 0; i < 14; i++) await SendAsync(silence);
        await call.RequestStream.CompleteAsync();
        var receipt = await call.ResponseAsync;
        if ((ulong)receipt.Samples != index || receipt.SourceEpoch == 0)
            throw new InvalidDataException("Incorrect IQ receipt.");
        var cursor = new SignalCursor();
        bool found = false;
        while (output.TryReadNext(cursor, out var frame) && frame is not null)
        {
            using var json = JsonDocument.Parse(frame.Payload);
            var root = json.RootElement;
            if (root.GetProperty("state").GetString() != "frame" ||
                root.GetProperty("sourceEpoch").GetUInt64() != receipt.SourceEpoch ||
                root.GetProperty("crcValid").ValueKind != JsonValueKind.True) continue;
            byte[] decoded = Convert.FromHexString(root.GetProperty("hex").GetString() ?? "");
            if (decoded.Length >= 5 && decoded[0] >> 4 == type &&
                BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(1, 4)) == clock) found = true;
        }
        if (!found) throw new InvalidDataException($"Missing CRC-valid recorded UNNE type {type}, clock {clock}.");
        Console.WriteLine($"PASS: recorded UNNE-1B type {type} clock {clock}, CRC valid, IQ IPC epoch {receipt.SourceEpoch}, {receipt.Samples} samples.");
        config.Enabled = false;
        await dsp.ConfigureTelemetryAsync(config, cancellationToken: ct);
    }

    private static byte[] Resample(byte[] source, double center)
    {
        if (source.Length < 80000 || source.Length % 8 != 0) throw new InvalidDataException("Invalid captured CF32.");
        int count = (int)Math.Floor((source.Length / 8 - 1.0) * 48000 / 50000);
        var result = new byte[count * 8];
        for (int i = 0; i < count; i++)
        {
            double position = i * 50000.0 / 48000;
            int j = (int)position * 8;
            double fraction = position - (int)position;
            double re = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(j)) * (1 - fraction) +
                BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(j + 8)) * fraction;
            double im = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(j + 4)) * (1 - fraction) +
                BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(j + 12)) * fraction;
            double phase = -2 * Math.PI * center * i / 48000;
            (double sin, double cos) = Math.SinCos(phase);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(i * 8), (float)(re * cos - im * sin));
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(i * 8 + 4), (float)(re * sin + im * cos));
        }
        return result;
    }
}
