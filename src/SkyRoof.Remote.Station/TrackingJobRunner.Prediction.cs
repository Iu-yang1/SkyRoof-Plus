using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class TrackingJobRunner
{
    internal async Task<TrackingSnapshot> PredictAtAsync(TrackingJobPredictionRequest input, CancellationToken ct)
    {
        TrackingRequest request;
        await _gate.WaitAsync(ct);
        try
        {
            if (_request is null || !_status.Running || input.JobId != _status.JobId)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Tracking job stopped or replaced."));
            request = _request.Tracking.Clone();
        }
        finally { _gate.Release(); }
        request.UtcUnixMilliseconds = input.UtcUnixMilliseconds;
        if (request.CelestialTarget != "MOON" || request.UplinkHz <= 0)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Select an active Moon tracking job with an uplink."));
        var result = tracking.Calculate(request);
        if (result.ElevationDegrees < request.ElevationMaskDegrees)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Moon is below the selected tracking job elevation mask."));
        return result;
    }
}
