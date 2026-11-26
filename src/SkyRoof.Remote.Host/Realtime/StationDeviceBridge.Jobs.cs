using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteTrackingJobStatus(string JobId, bool Running, string Phase,
    long CompletedTicks, RemoteTrackingStatus? Latest, string Error, bool ResumeAfterRestart,
    RemoteRadioLink? ActiveRadioLink = null, string CelestialTarget = "", bool PointRotator = false);

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

    public async Task<RemoteTrackingJobStatus> TuneTrackingJobAsync(RemoteTrackingTune request, CancellationToken ct)
    {
        try { return MapJob(await Jobs.TuneAsync(request.ToIpc(), deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: ct)); }
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

    internal async Task<RemoteTrackingStatus> PredictTrackingJobAtAsync(string jobId, long utcMilliseconds, CancellationToken ct)
    {
        var result = await Jobs.PredictAtAsync(new TrackingJobPredictionRequest
        {
            JobId = jobId, UtcUnixMilliseconds = utcMilliseconds
        }, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);
        return MapTrackingStatus(result);
    }

    private static RemoteTrackingJobStatus MapJob(TrackingJobStatus status) =>
        new(status.JobId, status.Running, status.Phase, status.CompletedTicks,
            status.Latest is null ? null : MapTrackingStatus(status.Latest),
            status.Error, status.ResumeAfterRestart,
            status.ActiveRadioLink is { } link ? RemoteRadioLink.FromIpc(link) : null,
            status.CelestialTarget, status.PointRotator);
}
