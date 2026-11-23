using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
if (args.Length is < 1 or > 3)
    throw new ArgumentException("Media.dll [Host.dll] [Device.dll] required.");
var dll = Path.GetFullPath(args[0]);
var hostDll = args.Length >= 2 ? Path.GetFullPath(args[1]) : null;
var deviceDll = args.Length == 3 ? Path.GetFullPath(args[2]) : null;
var suffix = Guid.NewGuid().ToString("N");
var pipe = "skyroof-media-test-" + suffix;
var bus = "skyroof-media-rx-" + suffix;
var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
info.ArgumentList.Add(dll);
info.Environment["SKYROOF_MEDIA_PIPE"] = pipe;
info.Environment["SKYROOF_RX_AUDIO_BUS"] = bus;

using var process = Process.Start(info) ?? throw new InvalidOperationException("Media start failed.");
HostMediaStatusProbe? hostProbe = null;
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
    if (hostDll is not null)
        hostProbe = await HostMediaStatusProbe.StartAsync(hostDll, pipe, suffix);
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
    // Wait until the first writer's frame is actually published. An awaited
    // client-side WriteAsync alone does not prove the server has processed it.
    var oldWriterCursor = new SignalCursor();
    var firstSourceObserved = false;
    for (var attempt = 0; attempt < 50; attempt++)
    {
        if (ring.TryReadNext(oldWriterCursor, out var first) &&
            first is not null && first.FirstSampleIndex == 1000)
        {
            firstSourceObserved = true;
            break;
        }
        await Task.Delay(50);
    }
    Check(firstSourceObserved, "Original media source never reached SignalBus.");
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
    if (hostProbe is not null)
    {
        var switchedStatus = await hostProbe.ReadAsync();
        Check(switchedStatus.GetProperty("epoch").GetUInt64() == takeover.Epoch &&
            switchedStatus.GetProperty("source").GetString() == "pcm16" &&
            !switchedStatus.GetProperty("streamActive").GetBoolean() &&
            switchedStatus.GetProperty("framesWritten").GetInt64() == 1 &&
            switchedStatus.GetProperty("samplesSkipped").GetUInt64() == 0,
            "Superseded producer mutated the new Media source snapshot");
    }

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

    // Authenticated Icom LAN session already negotiated by the source process:
    // exercise real 24-byte UDP framing -> Media gRPC -> shared RX SignalBus.
    const uint icomSender = 0x12345678, icomReceiver = 0xA1B2C3D4;
    byte[] audioBytes = Enumerable.Range(0, 960).Select(i => (byte)(i % 253)).ToArray();
    using (var icom = client.PublishIcomRx(deadline: DateTime.UtcNow.AddSeconds(15)))
    {
        await icom.RequestStream.WriteAsync(new IcomRxDatagram
        {
            FirstSampleIndex = 0, SampleRate = 48000, SenderSid = icomSender,
            ReceiverSid = icomReceiver, Codec = 4,
            Datagram = ByteString.CopyFrom(IcomPacket(audioBytes, icomSender, icomReceiver, 10))
        });
        await icom.RequestStream.WriteAsync(new IcomRxDatagram
        {
            FirstSampleIndex = 480, SampleRate = 48000, SenderSid = icomSender,
            ReceiverSid = icomReceiver, Codec = 4,
            Datagram = ByteString.CopyFrom(IcomPacket(audioBytes, icomSender, icomReceiver, 11))
        });
        await icom.RequestStream.CompleteAsync();
        var receipt = await icom.ResponseAsync;
        Check(receipt.FramesWritten == 2 && receipt.SamplesSkipped == 0 &&
            receipt.Epoch == ring.StreamEpoch, "Icom LAN RX packets failed Media ingestion");
    }
    var icomCursor = new SignalCursor();
    Check(ring.TryReadNext(icomCursor, out var icomFirst) && icomFirst is not null &&
        icomFirst.SampleCount == 480 && icomFirst.FirstSampleIndex == 0 &&
        icomFirst.Payload.SequenceEqual(audioBytes),
        "Icom LAN packet bytes were altered before SignalBus");
    Check(ring.TryReadNext(icomCursor, out var icomSecond) && icomSecond is not null &&
        icomSecond.FirstSampleIndex == 480 &&
        icomSecond.Payload.SequenceEqual(audioBytes), "Second Icom LAN packet missing");

    using (var badSid = client.PublishIcomRx(deadline: DateTime.UtcNow.AddSeconds(10)))
    {
        var spoofed = new IcomRxDatagram
        {
            FirstSampleIndex = 0, SampleRate = 48000,
            SenderSid = icomSender + 1, ReceiverSid = icomReceiver, Codec = 4,
            Datagram = ByteString.CopyFrom(IcomPacket(audioBytes, icomSender, icomReceiver, 12))
        };
        var rejectedSid = false;
        try
        {
            await badSid.RequestStream.WriteAsync(spoofed);
            await badSid.RequestStream.CompleteAsync();
            _ = await badSid.ResponseAsync;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        { rejectedSid = true; }
        Check(rejectedSid, "Icom LAN sender SID mismatch was accepted");
    }

    // Negotiated 44.1 kHz Icom audio must be converted to SignalBus 48 kHz,
    // retaining the continuous output clock across independent UDP datagrams.
    using (var resampled = client.PublishIcomRx(deadline: DateTime.UtcNow.AddSeconds(15)))
    {
        for (var frameIndex = 0; frameIndex < 6; frameIndex++)
        {
            byte[] source = new byte[441 * 2];
            for (int i = 0; i < 441; i++)
            {
                short value = (short)Math.Round(12000 * Math.Sin(
                    2 * Math.PI * 1000 * (frameIndex * 441 + i) / 44100));
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(i * 2, 2), value);
            }
            await resampled.RequestStream.WriteAsync(new IcomRxDatagram
            {
                FirstSampleIndex = (ulong)(frameIndex * 441), SampleRate = 44100,
                SenderSid = icomSender, ReceiverSid = icomReceiver, Codec = 4,
                Datagram = ByteString.CopyFrom(IcomPacket(source, icomSender,
                    icomReceiver, (ushort)(20 + frameIndex)))
            });
        }
        await resampled.RequestStream.CompleteAsync();
        var receipt = await resampled.ResponseAsync;
        Check(receipt.FramesWritten >= 5 && receipt.SamplesSkipped == 0,
            "44.1 kHz streaming conversion lost its continuous sample timeline");
    }
    var resampledCursor = new SignalCursor();
    ulong nextIndex = 0;
    int convertedCount = 0;
    while (ring.TryReadNext(resampledCursor, out var frame) && frame is not null)
    {
        Check(frame.SampleRate == 48000 && frame.FirstSampleIndex == nextIndex,
            "ASRC output sample clock was discontinuous");
        nextIndex += (ulong)frame.SampleCount;
        convertedCount++;
    }
    Check(convertedCount >= 5 && nextIndex is > 2700 and < 2900,
        "44.1 kHz Icom samples did not become 48 kHz PCM");

    // UDP outer sequence must determine presentation order, not gRPC arrival order.
    using (var reordering = client.PublishIcomRx(deadline: DateTime.UtcNow.AddSeconds(15)))
    {
        foreach (var (sequence, index, seed) in new (ushort, ulong, byte)[]
        {
            (50, 0, 3), (52, 960, 5), (51, 480, 7), (51, 480, 7)
        })
        {
            byte[] payload = Enumerable.Repeat(seed, 960).ToArray();
            await reordering.RequestStream.WriteAsync(new IcomRxDatagram
            {
                FirstSampleIndex = index, SampleRate = 48000,
                SenderSid = icomSender, ReceiverSid = icomReceiver, Codec = 4,
                Datagram = ByteString.CopyFrom(IcomPacket(
                    payload, icomSender, icomReceiver, sequence))
            });
        }
        await reordering.RequestStream.CompleteAsync();
        var receipt = await reordering.ResponseAsync;
        Check(receipt.FramesWritten == 3 && receipt.SamplesSkipped == 0,
            "Reordered Icom LAN UDP packets were lost or duplicated");
    }
    var reorderedCursor = new SignalCursor();
    foreach (var (index, seed) in new (ulong, byte)[] { (0, 3), (480, 7), (960, 5) })
    {
        Check(ring.TryReadNext(reorderedCursor, out var reorderedFrame) && reorderedFrame is not null &&
            reorderedFrame.FirstSampleIndex == index &&
            reorderedFrame.Payload.All(value => value == seed),
            "Icom LAN UDP reorder did not preserve the original PCM sample timeline");
    }

    using (var missing = client.PublishIcomRx(deadline: DateTime.UtcNow.AddSeconds(15)))
    {
        foreach (var (sequence, index) in new (ushort, ulong)[] { (60, 0), (62, 960) })
            await missing.RequestStream.WriteAsync(new IcomRxDatagram
            {
                FirstSampleIndex = index, SampleRate = 48000,
                SenderSid = icomSender, ReceiverSid = icomReceiver, Codec = 4,
                Datagram = ByteString.CopyFrom(IcomPacket(
                    audioBytes, icomSender, icomReceiver, sequence))
            });
        await missing.RequestStream.CompleteAsync();
        var receipt = await missing.ResponseAsync;
        Check(receipt.FramesWritten == 2 && receipt.SamplesSkipped == 480,
            "A missing Icom LAN packet did not preserve the 480-sample gap");
    }
    // Keep the producer stream open during an actual missing-packet timeout.
    // The Media worker must flush buffered seq 72 without waiting for seq 71,
    // a newer packet, or PublishIcomRx completion.
    using (var live = client.PublishIcomRx(deadline: DateTime.UtcNow.AddSeconds(15)))
    {
        async Task SendLive(ushort sequence, ulong index, byte value)
        {
            var pcm = Enumerable.Repeat(value, 960).ToArray();
            await live.RequestStream.WriteAsync(new IcomRxDatagram
            {
                FirstSampleIndex = index, SampleRate = 48000,
                SenderSid = icomSender, ReceiverSid = icomReceiver, Codec = 4,
                Datagram = ByteString.CopyFrom(IcomPacket(
                    pcm, icomSender, icomReceiver, sequence))
            });
        }

        await SendLive(70, 0, 11);
        await SendLive(72, 960, 22);
        var liveCursor = new SignalCursor();
        var receivedFirst = false;
        var receivedAfterGap = false;
        for (int attempt = 0; attempt < 100 && !receivedAfterGap; attempt++)
        {
            while (ring.TryReadNext(liveCursor, out var liveFrame) &&
                   liveFrame is not null)
            {
                if (liveFrame.FirstSampleIndex == 0 &&
                    liveFrame.Payload.All(value => value == 11))
                    receivedFirst = true;
                if (liveFrame.FirstSampleIndex == 960 &&
                    liveFrame.Payload.All(value => value == 22))
                    receivedAfterGap = true;
            }
            if (!receivedAfterGap) await Task.Delay(25);
        }
        Check(receivedFirst && receivedAfterGap,
            "Media jitter timeout did not release seq 72 on an open gRPC stream");
        RxStreamReceipt? liveStatus = null;
        for (int probe = 0; probe < 30; probe++)
        {
            liveStatus = await client.GetRxStatusAsync(new RxStatusQuery());
            if (liveStatus.DatagramsReceived == 2 &&
                liveStatus.DatagramsMissing == 1) break;
            await Task.Delay(20);
        }
        Check(liveStatus is not null && liveStatus.StreamActive &&
            liveStatus.Source == "icom-lan" &&
            liveStatus.DatagramsReceived == 2 && liveStatus.DatagramsMissing == 1 &&
            liveStatus.SamplesSkipped == 480,
            "Live Icom status did not report active stream and observed UDP loss");
        Check(!live.ResponseAsync.IsCompleted,
            "Media closed the source stream to flush buffered packets");
        if (hostProbe is not null)
        {
            var hostStatus = await hostProbe.WaitForActiveLossAsync(ring.StreamEpoch);
            Check(hostStatus.GetProperty("source").GetString() == "icom-lan" &&
                hostStatus.GetProperty("datagramsReceived").GetUInt64() == 2 &&
                hostStatus.GetProperty("samplesSkipped").GetUInt64() == 480,
                "Authenticated Host proxy disagrees with live Media gRPC diagnostics");
        }

        await SendLive(71, 480, 77); // Arrived too late; never overwrite seq 72.
        await SendLive(73, 1440, 33);
        await live.RequestStream.CompleteAsync();
        var result = await live.ResponseAsync;
        Check(result.FramesWritten == 3 && result.SamplesSkipped == 480 &&
            result.DatagramsReceived == 4 && result.DatagramsDropped == 1 &&
            result.DatagramsMissing == 1 && !result.StreamActive &&
            result.Source == "icom-lan",
            "Timed-out packet gap and transport diagnostics were not retained");
        Check(ring.TryReadNext(liveCursor, out var postTimeout) &&
            postTimeout is not null &&
            postTimeout.FirstSampleIndex == 1440 &&
            postTimeout.Payload.All(value => value == 33),
            "Post-timeout source frame missing or late packet incorrectly accepted");
    }
    var completedStatus = await client.GetRxStatusAsync(new RxStatusQuery());
    Check(!completedStatus.StreamActive && completedStatus.DatagramsMissing == 1 &&
        completedStatus.Source == "icom-lan",
        "Completed Icom stream continued to report an active producer");
    Console.WriteLine("PASS: Media flushes missing UDP packets while source gRPC remains open.");
    if (deviceDll is not null)
        await DeviceIcomAudioProbe.RunAsync(deviceDll, pipe, suffix, ring, client);
    Console.WriteLine("PASS: Icom LAN cross-process reorder, duplicate suppression and missing-sample gap.");
    Console.WriteLine("PASS: continuous 44.1 kHz Icom RX ASRC to 48 kHz SignalBus.");
    Console.WriteLine("PASS: Icom LAN UDP packet -> Media gRPC -> SignalBus PCM16 and SID rejection.");
    Console.WriteLine("PASS: Media process RX PCM16 ingestion, SignalBus bytes/index, loss count, source takeover and format validation.");
}
finally
{
    if (hostProbe is not null) await hostProbe.DisposeAsync();
    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
}
static void Check(bool valid, string message)
{
    if (!valid) throw new InvalidOperationException(message);
}

static byte[] IcomPacket(byte[] pcm, uint sender, uint receiver, ushort seq)
{
    byte[] packet = new byte[24 + pcm.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), seq);
    BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), sender);
    BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), receiver);
    packet[16] = 0x80;
    BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), seq);
    pcm.CopyTo(packet, 24);
    return packet;
}
