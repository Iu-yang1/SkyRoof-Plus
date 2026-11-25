using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class TrackingJobsService(TrackingJobRunner runner) : StationTrackingJobs.StationTrackingJobsBase
{
    public override Task<TrackingJobStatus> Start(TrackingJobStart request, ServerCallContext context) =>
        runner.StartJobAsync(request, context.CancellationToken);

    public override Task<TrackingJobStatus> Status(TrackingJobQuery request, ServerCallContext context) =>
        runner.GetStatusAsync(context.CancellationToken);

    public override Task<TrackingSnapshot> PredictAt(TrackingJobPredictionRequest request, ServerCallContext context) =>
        runner.PredictAtAsync(request, context.CancellationToken);

    public override Task<TrackingJobStatus> Stop(TrackingJobStop request, ServerCallContext context) =>
        runner.StopJobAsync(request.JobId, context.CancellationToken);
}
