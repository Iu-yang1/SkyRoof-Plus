using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class StationAfcService(TrackingJobRunner runner) : StationAfc.StationAfcBase
{
    public override Task<AfcSessionStatus> Open(AfcOpenRequest request, ServerCallContext context) =>
        runner.OpenAfcAsync(request, context.CancellationToken);
    public override Task<AfcSessionStatus> Update(AfcUpdateRequest request, ServerCallContext context) =>
        runner.UpdateAfcAsync(request, context.CancellationToken);
    public override Task<AfcSessionStatus> Close(AfcCloseRequest request, ServerCallContext context) =>
        runner.CloseAfcAsync(request.SessionId, context.CancellationToken);
}
