using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;
using Grpc.Net.Client;
using NAudio.Wave;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

internal static partial class TelemetryLiveProbe
{
    public static async Task RunAsync(NamedSignalRing rx, string resultsName, string pipe, string corpus, string archive)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
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
        var config = new TelemetryRxConfiguration { Enabled = true, NoradId = 39444, Satellite = "AO-73",
            Modulation = "BPSK", Framing = "AO40FEC", Baud = 1200, Input = "tone-audio", CenterHz = 1700, Differential = true, SaveFrames = true, KissPort = ReserveKissPort() };
        await dsp.ConfigureTelemetryAsync(config, cancellationToken: ct);
        using var output = new NamedSignalRing(resultsName, 64, 262144);
        using var kiss = new TcpClient();
        await kiss.ConnectAsync("127.0.0.1", config.KissPort, ct);
        var wire = ReadKissAsync(kiss.GetStream(), ct);
        rx.ResetEpoch(91);
        await ReplayAsync(rx, Path.Combine(corpus, "ao73.wav"), ct);
        config.Enabled = false;
        await dsp.ConfigureTelemetryAsync(config, cancellationToken: ct);
        byte[] decoded = VerifyResults(output);
        byte[] received = await wire;
        VerifyOutputs(decoded, received, archive);
        config.KissPort = 0;
        await dsp.ConfigureTelemetryAsync(config, cancellationToken: ct);
        Console.WriteLine("PASS: real AO-73 decoded bytes equal station BIN archive and escaped KISS TCP payload; STOP flush persisted LOG.");
    }

    private static byte[] VerifyResults(NamedSignalRing output)
    {
        var cursor = new SignalCursor();
        byte[]? verified = null;
        while (output.TryReadNext(cursor, out var frame) && frame is not null)
        {
            using var json = JsonDocument.Parse(frame.Payload);
            var root = json.RootElement;
            if (root.GetProperty("state").GetString() != "frame") continue;
            if (root.GetProperty("sourceEpoch").GetUInt64() != 91 ||
                root.GetProperty("config").GetProperty("noradId").GetInt32() != 39444) continue;
            if (root.GetProperty("crcValid").ValueKind != JsonValueKind.True || root.GetProperty("length").GetInt32() != 256) continue;
            if (root.GetProperty("hex").GetString()?.Length != 512 ||
                !root.GetProperty("fields").EnumerateArray().Any(field => field.GetProperty("name").GetString() == "Frame header"))
                throw new InvalidDataException("Telemetry frame bytes or decoded fields missing.");
            verified = Convert.FromHexString(root.GetProperty("hex").GetString() ?? "");
        }
        if (verified is null) throw new InvalidDataException("Deployed DSP did not recover original AO-73 frame.");
        Console.WriteLine("PASS: real AO-73 recorded RF -> PCM SignalBus -> independent SkyTlm DSP -> verified 256-byte frame and engineering fields.");
        return verified;
    }

    private static async Task ReplayAsync(NamedSignalRing rx, string path, CancellationToken ct)
    {
        using var source = new AudioFileReader(path);
        if (source.WaveFormat.SampleRate != 48000 || source.WaveFormat.Channels != 1)
            throw new InvalidDataException("Expected 48 kHz mono recording.");
        var samples = new float[960];
        ulong index = 0;
        int count;
        while ((count = source.Read(samples, 0, samples.Length)) > 0)
        {
            var pcm = new byte[count * 2];
            int offset = 0;
            foreach (float sample in samples.AsSpan(0, count))
            {
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset), (short)Math.Clamp(Math.Round(sample * 32768.0), -32768, 32767));
                offset += 2;
            }
            rx.Publish(index, Stopwatch.GetTimestamp(), 48000, count, pcm);
            index += (ulong)count;
            await Task.Delay(20, ct);
        }
        // Allow the independent DSP reader to consume the final ring publication before STOP.
        await Task.Delay(500, ct);
    }
}
