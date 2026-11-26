using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class TrackingJobRunner
{
    private StationAfcSession? _afc;

    internal async Task<AfcSessionStatus> OpenAfcAsync(AfcOpenRequest input, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_request is null || !_status.Running || input.JobId != _status.JobId)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Select the active tracking job."));
            if (_afc is { } current && !current.Expired(DateTime.UtcNow))
                throw new RpcException(new Status(StatusCode.AlreadyExists, "Another IF source owns this tracking AFC session."));
            try { _afc = new StationAfcSession(input); }
            catch (ArgumentException error) { throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message)); }
            if (_status.Latest is { } latest) _afc.Readback(latest);
            return _afc.Status();
        }
        finally { _gate.Release(); }
    }

    internal async Task<AfcSessionStatus> UpdateAfcAsync(AfcUpdateRequest input, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            if (_afc is not { } active || active.Id != input.SessionId || active.JobId != _status.JobId || active.Expired(now))
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "AFC session expired or tracking target changed."));
            return active.Update(input.Measurement, now);
        }
        finally { _gate.Release(); }
    }

    internal async Task<AfcSessionStatus> CloseAfcAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_afc?.Id == id) { _afc = null; _nextTick = 0; }
            return new AfcSessionStatus { SessionId = id, State = "Off" };
        }
        finally { _gate.Release(); }
    }

    private void ComposeAfc(TrackingSnapshot prediction)
    {
        var now = DateTime.UtcNow;
        if (_afc is { } active)
        {
            if (active.Expired(now) || active.JobId != _status.JobId) _afc = null;
            else active.Apply(prediction, now);
        }
    }
}
