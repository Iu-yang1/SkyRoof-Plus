using Grpc.Core;
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
        var decoder = new IcomLanRxStreamDecoder(publisher, epoch);
        while (await requestStream.MoveNext(context.CancellationToken))
            decoder.Accept(requestStream.Current);
        decoder.Complete();
        return publisher.Complete(epoch);
    }
}
