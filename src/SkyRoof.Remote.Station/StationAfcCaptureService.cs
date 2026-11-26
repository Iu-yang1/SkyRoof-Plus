using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class StationAfcCaptureService(StationAfcCaptureWorker worker) : StationAfcCapture.StationAfcCaptureBase
{
    public override Task<AfcCaptureStatus> Start(AfcCaptureStart request, ServerCallContext context) =>
        worker.BeginAsync(request, context.CancellationToken);
    public override Task<AfcCaptureStatus> Status(AfcCaptureQuery request, ServerCallContext context) =>
        Task.FromResult(worker.Snapshot());
    public override Task<AfcCaptureStatus> Retarget(AfcCaptureTarget request, ServerCallContext context) =>
        Task.FromResult(worker.Retarget(request));
    public override Task<AfcCaptureStatus> Stop(AfcCaptureQuery request, ServerCallContext context) =>
        worker.EndAsync(context.CancellationToken);
}
