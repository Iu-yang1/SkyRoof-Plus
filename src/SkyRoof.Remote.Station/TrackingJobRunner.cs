using System.Diagnostics;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class TrackingJobRunner(TrackingService tracking, DeviceControlForwarder device,
    TrackingJobStore store, ILogger<TrackingJobRunner> logger) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TrackingJobStart? _request;
    private TrackingJobStatus _status = new() { Phase = "idle" };
    private long _nextTick;
    private bool _rotatorNeedsStop;

    public async Task<TrackingJobStatus> StartJobAsync(TrackingJobStart request, CancellationToken ct)
    {
        Validate(request);
        await _gate.WaitAsync(ct);
        try
        {
            if (_rotatorNeedsStop) await StopRotatorAsync(ct);
            var id = Guid.NewGuid().ToString("N");
            if (request.ResumeAfterRestart) store.Save(id, request);
            else store.Clear();
            Activate(id, request);
            return Snapshot();
        }
        finally { _gate.Release(); }
    }

    public async Task<TrackingJobStatus> GetStatusAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return Snapshot(); }
        finally { _gate.Release(); }
    }

    public async Task<TrackingJobStatus> StopJobAsync(string jobId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (jobId != _status.JobId) return Snapshot();
            store.Clear();
            _request = null;
            _status.Running = false;
            _status.Phase = "stopped";
            if (_rotatorNeedsStop)
            {
                try { await StopRotatorAsync(ct); }
                catch (RpcException ex) { _status.Error = ex.Status.Detail; }
            }
            return Snapshot();
        }
        finally { _gate.Release(); }
    }

    private TrackingJobStatus Snapshot()
    {
        var result = _status.Clone();
        if (_request?.Tracking is not { } current) return result;
        result.CelestialTarget = current.CelestialTarget;
        result.PointRotator = current.PointRotator;
        if (current.CelestialTarget.Length == 0)
            result.ActiveRadioLink = current.RadioLink?.Clone() ?? new RadioLinkProfile
            {
                DownlinkLowHz = current.DownlinkHz, UplinkLowHz = current.UplinkHz
            };
        return result;
    }

    private void Activate(string id, TrackingJobStart request)
    {
        _request = request.Clone();
        _status = new TrackingJobStatus
        {
            JobId = id, Running = true, Phase = "starting",
            ResumeAfterRestart = request.ResumeAfterRestart
        };
        _nextTick = 0;
    }

    private void Validate(TrackingJobStart request)
    {
        if (request.Tracking is null || request.IntervalMilliseconds < 100)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Tracking configuration and an interval of at least 100 ms are required."));
        var current = request.Tracking.Clone();
        current.UtcUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try { tracking.Calculate(current); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message)); }
    }
}
