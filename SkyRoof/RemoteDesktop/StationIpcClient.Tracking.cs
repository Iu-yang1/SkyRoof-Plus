using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal Task<CatalogSearchResult> SearchCatalogAsync(string query, CancellationToken token) =>
        catalog.SearchAsync(new CatalogSearchRequest { Query = query, Limit = 50 }, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: token).ResponseAsync;

    internal Task<TrackingSnapshot> PredictAsync(TrackingRequest request, CancellationToken token) =>
        predictor.PredictAsync(request, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: token).ResponseAsync;

    internal Task<TrackingJobStatus> StartTrackingAsync(TrackingRequest request, bool resume, CancellationToken token) =>
        tracking.StartAsync(new TrackingJobStart { Tracking = request, IntervalMilliseconds = 1000, ResumeAfterRestart = resume },
            deadline: DateTime.UtcNow.AddSeconds(12), cancellationToken: token).ResponseAsync;

    internal Task<TrackingJobStatus> TuneTrackingAsync(TrackingJobTune request, CancellationToken token) =>
        tracking.TuneAsync(request, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: token).ResponseAsync;

    internal Task<TrackingJobStatus> StopTrackingAsync(string jobId, CancellationToken token) =>
        tracking.StopAsync(new TrackingJobStop { JobId = jobId }, deadline: DateTime.UtcNow.AddSeconds(12), cancellationToken: token).ResponseAsync;

    internal Task<RotatorPosition> ParkAsync(RotatorParkRequest request, CancellationToken token) =>
        rotator.ParkAsync(request, deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: token).ResponseAsync;
}
