using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class TrackingJobRunner
{
    internal async Task<RotatorPosition> StopPointingAsync(CancellationToken ct)
    {
        string? jobId = _pointingJob;
        if (jobId is null) return await SendStopAsync(ct);

        // Suppress subsequent automatic POINT immediately, even while a
        // tracking tick is blocked on CAT or rotator feedback.
        _suspendedPointingJob = jobId;
        RotatorPosition? immediate = null;
        try { immediate = await SendStopAsync(CancellationToken.None); }
        catch (RpcException error) { logger.LogWarning(error, "Initial tracking rotator STOP failed; retry after draining tick"); }

        // Complete the state change even if the initiating client disconnects.
        // The gate drains any tick which was already dispatching a point.
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_request is null || _status.JobId != jobId || !_request.Tracking.PointRotator)
                return immediate ?? await SendStopAsync(CancellationToken.None);
            return await CommitPointingStopAsync();
        }
        finally { _gate.Release(); }
    }

    private async Task<RotatorPosition> CommitPointingStopAsync()
    {
        var updated = _request?.Clone() ?? throw new InvalidOperationException("Tracking job unavailable.");
        updated.Tracking.PointRotator = false;
        _request = updated;
        _pointingJob = null;
        _rotatorNeedsStop = true;
        RotatorPosition final;
        try
        {
            if (updated.ResumeAfterRestart) store.Save(_status.JobId, updated);
        }
        finally
        {
            // Final STOP closes the race with a POINT already in flight
            // when the first urgent STOP was dispatched.
            final = await SendStopAsync(CancellationToken.None);
            _rotatorNeedsStop = false;
        }
        _status.Latest = null;
        _nextTick = 0;
        return final;
    }

    private async Task<RotatorPosition> SendStopAsync(CancellationToken ct) =>
        await device.Rotator.StopAsync(new RotatorRead(), deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: ct);
}
