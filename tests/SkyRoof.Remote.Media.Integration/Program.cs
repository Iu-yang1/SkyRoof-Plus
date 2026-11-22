using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
var dll = Path.GetFullPath(args.Single());
var suffix = Guid.NewGuid().ToString("N");
var pipe = "skyroof-media-test-" + suffix;
var bus = "skyroof-media-rx-" + suffix;
var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
info.ArgumentList.Add(dll);
info.Environment["SKYROOF_MEDIA_PIPE"] = pipe;
info.Environment["SKYROOF_RX_AUDIO_BUS"] = bus;

using var process = Process.Start(info) ?? throw new InvalidOperationException("Media start failed.");
try
{
    NamedSignalRing? signal = null;
    for (var attempt = 0; attempt < 60; attempt++)
    {
        try
        {
            signal = new NamedSignalRing(bus, slots: 64, payloadCapacity: 4096);
            break;
        }
        catch (InvalidDataException) { await Task.Delay(100); }
    }
    using var ring = signal ?? throw new TimeoutException("Media SignalBus did not initialize.");
    using var handler = new SocketsHttpHandler
    {
        ConnectCallback = async (_, ct) =>
        {
            var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.None);
            try { await stream.ConnectAsync(3000, ct); return stream; }
            catch { await stream.DisposeAsync(); throw; }
        }
    };
    using var channel = GrpcChannel.ForAddress("http://localhost",
        new GrpcChannelOptions { HttpHandler = handler });
    var client = new MediaIngress.MediaIngressClient(channel);
    static RxPcmChunk Packet(ulong index, byte seed = 0) => new()
    {
        FirstSampleIndex = index, SampleRate = 48000, Channels = 1,
        SamplesPerChannel = 480,
        Pcm16 = ByteString.CopyFrom(Enumerable.Range(0, 960)
            .Select(i => (byte)((i + seed) % 251)).ToArray())
    };

    using (var call = client.PublishRx(deadline: DateTime.UtcNow.AddSeconds(15)))
    {
        await call.RequestStream.WriteAsync(Packet(0, 9));
        await call.RequestStream.WriteAsync(Packet(960, 19));
        await call.RequestStream.CompleteAsync();
        var result = await call.ResponseAsync;
        Check(result.FramesWritten == 2 && result.SamplesSkipped == 480 &&
            result.State == "completed" && result.Epoch == ring.StreamEpoch,
            "Media did not preserve gapped source sample indices");
    }
    var cursor = new SignalCursor();
    Check(ring.TryReadNext(cursor, out var a) && a is not null &&
        a.SampleRate == 48000 && a.SampleCount == 480 && a.FirstSampleIndex == 0 &&
        a.Payload.SequenceEqual(Packet(0, 9).Pcm16.ToByteArray()),
        "Published PCM16 first frame did not survive cross-process SignalBus");
    Check(ring.TryReadNext(cursor, out var b) && b is not null &&
        b.FirstSampleIndex == 960 && b.Payload.SequenceEqual(Packet(960, 19).Pcm16.ToByteArray()),
        "Published PCM16 second frame had wrong samples or index");

    using var older = client.PublishRx(deadline: DateTime.UtcNow.AddSeconds(15));
    await older.RequestStream.WriteAsync(Packet(1000));
    using var newer = client.PublishRx(deadline: DateTime.UtcNow.AddSeconds(15));
    await newer.RequestStream.WriteAsync(Packet(0, 99));
    await newer.RequestStream.CompleteAsync();
    var takeover = await newer.ResponseAsync;
    Check(takeover.FramesWritten == 1 && takeover.Epoch == ring.StreamEpoch,
        "New audio producer did not reset stream epoch");
    var rejected = false;
    try
    {
        await older.RequestStream.WriteAsync(Packet(1480));
        await older.RequestStream.CompleteAsync();
        _ = await older.ResponseAsync;
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Aborted) { rejected = true; }
    Check(rejected, "Superseded RX audio source was able to alter live SignalBus");

    var fresh = new SignalCursor();
    Check(ring.TryReadNext(fresh, out var latest) && latest is not null &&
        latest.StreamEpoch == takeover.Epoch &&
        latest.FirstSampleIndex == 0 &&
        latest.Payload.SequenceEqual(Packet(0, 99).Pcm16.ToByteArray()),
        "Takeover did not present only the new audio timeline to consumers");

    using var invalid = client.PublishRx(deadline: DateTime.UtcNow.AddSeconds(10));
    var invalidFormat = Packet(0);
    invalidFormat.SampleRate = 44100;
    var invalidRejected = false;
    try
    {
        await invalid.RequestStream.WriteAsync(invalidFormat);
        await invalid.RequestStream.CompleteAsync();
        _ = await invalid.ResponseAsync;
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument) { invalidRejected = true; }
    Check(invalidRejected, "Incorrect PCM format was silently published as 48kHz");
    Console.WriteLine("PASS: Media process RX PCM16 ingestion, SignalBus bytes/index, loss count, source takeover and format validation.");
}
finally
{
    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
}
static void Check(bool valid, string message)
{
    if (!valid) throw new InvalidOperationException(message);
}
