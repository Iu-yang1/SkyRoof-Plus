using System.IO.Pipes;
using System.Security.Principal;
using Google.Protobuf;
using Grpc.Net.Client;
using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

internal sealed partial class IcomLanReceiveWorker
{
    private async Task RunAudioToMediaAsync(IcomLanAudioSocket audio,
        int sampleRate, IcomLanAudioCodec codec, CancellationToken ct)
    {
        var pipe = Environment.GetEnvironmentVariable("SKYROOF_MEDIA_PIPE")
            ?? "skyroof-station-media";
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await client.ConnectAsync(3000, token); return client; }
                catch { await client.DisposeAsync(); throw; }
            }
        };
        using var channel = GrpcChannel.ForAddress("http://localhost",
            new GrpcChannelOptions { HttpHandler = handler });
        using var call = new MediaIngress.MediaIngressClient(channel)
            .PublishIcomRx(cancellationToken: ct);
        var clock = new IcomLanRxFrameClock();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
        var pending = audio.ReadAudioFrameAsync(codec, ct);
        var tick = timer.WaitForNextTickAsync(ct).AsTask();

        async Task PublishAsync(IReadOnlyList<IcomLanTimedRxFrame> ready)
        {
            foreach (var frame in ready)
                await call.RequestStream.WriteAsync(new IcomRxDatagram
                {
                    FirstSampleIndex = frame.FirstSampleIndex,
                    SampleRate = sampleRate,
                    SenderSid = audio.RemoteSid,
                    ReceiverSid = audio.LocalSid,
                    Codec = (int)codec,
                    Datagram = ByteString.CopyFrom(frame.Datagram)
                });
        }

        while (!ct.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(pending, tick);
            if (completed == pending)
            {
                var raw = await pending;
                if (!IcomLanAudioPacketCodec.TryDecode(raw, audio.RemoteSid,
                    audio.LocalSid, codec, out var frame))
                    throw new InvalidDataException("Audio socket returned an invalid LAN frame.");
                await PublishAsync(clock.Push(frame.OuterSequence,
                    frame.Pcm16, DateTime.UtcNow, raw));
                pending = audio.ReadAudioFrameAsync(codec, ct);
            }
            else
            {
                if (!await tick) break;
                await PublishAsync(clock.Flush(DateTime.UtcNow));
                tick = timer.WaitForNextTickAsync(ct).AsTask();
            }
        }
        await PublishAsync(clock.Flush(DateTime.UtcNow, force: true));
        await call.RequestStream.CompleteAsync();
        _ = await call.ResponseAsync;
    }
}
