using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Foundation.Media;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Media;

/// <summary>
/// Private Windows IPC RX audio ingestion. Public clients consume the independent
/// authenticated WSS/RTC path and never gain direct access to this service.
/// </summary>
public sealed class MediaIngressService(RxAudioPublisher publisher)
    : MediaIngress.MediaIngressBase
{
    public override async Task<RxStreamReceipt> PublishRx(
        IAsyncStreamReader<RxPcmChunk> requestStream, ServerCallContext context)
    {
        var epoch = publisher.OpenStream();
        while (await requestStream.MoveNext(context.CancellationToken))
            publisher.Publish(epoch, requestStream.Current);
        return publisher.Complete(epoch);
    }

    public override async Task<RxStreamReceipt> PublishIcomRx(
        IAsyncStreamReader<IcomRxDatagram> requestStream, ServerCallContext context)
    {
        var epoch = publisher.OpenStream();
        (uint Sender, uint Receiver, int Codec, int Rate)? session = null;
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var packet = requestStream.Current;
            // The current station SignalBus contract is 48 kHz PCM16. Other
            // negotiated Icom rates require an explicit streaming ASRC stage.
            if (packet.SampleRate != 48000)
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "Icom LAN RX must be negotiated at 48 kHz for the current SignalBus."));
            var identity = (packet.SenderSid, packet.ReceiverSid,
                packet.Codec, packet.SampleRate);
            if (session is not null && session.Value != identity)
                throw new RpcException(new Status(StatusCode.Aborted,
                    "Icom LAN audio stream changed its negotiated session."));
            if (!IcomLanAudioPacketCodec.TryDecode(packet.Datagram.Span,
                packet.SenderSid, packet.ReceiverSid,
                (IcomLanAudioCodec)packet.Codec, out var decoded))
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "Malformed Icom LAN audio frame or incorrect session ID."));
            session = identity;
            publisher.Publish(epoch, new RxPcmChunk
            {
                FirstSampleIndex = packet.FirstSampleIndex,
                SampleRate = packet.SampleRate,
                Channels = 1,
                SamplesPerChannel = decoded.Pcm16.Length / 2,
                Pcm16 = ByteString.CopyFrom(decoded.Pcm16)
            });
        }
        return publisher.Complete(epoch);
    }
}
