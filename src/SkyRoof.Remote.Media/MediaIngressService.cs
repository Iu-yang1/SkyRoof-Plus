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
        // An open gRPC source can go idle after a missing UDP packet. Keep one
        // outstanding MoveNext and retire the reorder deadline independently;
        // otherwise a buffered packet would wait for the next source packet or EOF.
        var nextPacket = requestStream.MoveNext(context.CancellationToken);
        while (true)
        {
            var completed = await Task.WhenAny(nextPacket,
                Task.Delay(25, context.CancellationToken));
            if (completed == nextPacket)
            {
                if (!await nextPacket) break;
                decoder.Accept(requestStream.Current);
                nextPacket = requestStream.MoveNext(context.CancellationToken);
            }
            else
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                decoder.FlushDue();
            }
        }
        decoder.Complete();
        return publisher.Complete(epoch);
    }
}
