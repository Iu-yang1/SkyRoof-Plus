using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteTrackingJobStatus(string JobId, bool Running, string Phase,
    long CompletedTicks, RemoteTrackingStatus? Latest, string Error, bool ResumeAfterRestart);

public sealed partial class StationDeviceBridge
{
    private StationTrackingJobs.StationTrackingJobsClient Jobs => new(_channel.Value);

    public async Task<RemoteTrackingJobStatus> StartTrackingJobAsync(RemoteTrackingRequest request,
        int intervalMilliseconds, bool resumeAfterRestart, CancellationToken ct)
    {
        try
        {
            return MapJob(await Jobs.StartAsync(new TrackingJobStart
            {
                Tracking = MapTrackingRequest(request),
                IntervalMilliseconds = intervalMilliseconds,
                ResumeAfterRestart = resumeAfterRestart
            }, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: ct));
        }
        catch (RpcException ex) { throw new HubException(ex.Status.Detail); }
    }

    public async Task<RemoteTrackingJobStatus> ReadTrackingJobAsync(CancellationToken ct)
    {
        try
        {
            return MapJob(await Jobs.StatusAsync(new TrackingJobQuery(),
                deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: ct));
        }
        catch (RpcException ex) { throw new HubException(ex.Status.Detail); }
    }

    public async Task<RemoteTrackingJobStatus> StopTrackingJobAsync(string jobId, CancellationToken ct)
    {
        try
        {
            return MapJob(await Jobs.StopAsync(new TrackingJobStop { JobId = jobId },
                deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: ct));
        }
        catch (RpcException ex) { throw new HubException(ex.Status.Detail); }
    }

    private static RemoteTrackingJobStatus MapJob(TrackingJobStatus status) =>
        new(status.JobId, status.Running, status.Phase, status.CompletedTicks,
            status.Latest is null ? null : MapTrackingStatus(status.Latest),
            status.Error, status.ResumeAfterRestart);
}
