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
        try
        {
            while (await requestStream.MoveNext(context.CancellationToken))
                publisher.Publish(epoch, requestStream.Current);
            return publisher.Complete(epoch);
        }
        finally { publisher.EndStream(epoch); }
    }

    public override Task<RxStreamReceipt> GetRxStatus(
        RxStatusQuery request, ServerCallContext context) =>
        Task.FromResult(publisher.Status());

    public override async Task<RxStreamReceipt> PublishIcomRx(
        IAsyncStreamReader<IcomRxDatagram> requestStream, ServerCallContext context)
    {
        var epoch = publisher.OpenStream("icom-lan");
        var decoder = new IcomLanRxStreamDecoder(publisher, epoch);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
            var next = requestStream.MoveNext(context.CancellationToken);
            var tick = timer.WaitForNextTickAsync(context.CancellationToken).AsTask();
            while (true)
            {
                var completed = await Task.WhenAny(next, tick);
                if (completed == next)
                {
                    if (!await next) break;
                    decoder.Accept(requestStream.Current);
                    next = requestStream.MoveNext(context.CancellationToken);
                }
                else
                {
                    if (!await tick) break;
                    decoder.FlushDue();
                    tick = timer.WaitForNextTickAsync(context.CancellationToken).AsTask();
                }
                publisher.NoteIcomDiagnostics(epoch, decoder.Diagnostics);
            }
            decoder.Complete();
            publisher.NoteIcomDiagnostics(epoch, decoder.Diagnostics);
            return publisher.Complete(epoch);
        }
        finally { publisher.EndStream(epoch); }
    }
}
