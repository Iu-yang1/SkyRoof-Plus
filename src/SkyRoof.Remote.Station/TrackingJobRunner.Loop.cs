using System.Diagnostics;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class TrackingJobRunner
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var saved = store.Load();
            if (saved is { } job && job.Request.ResumeAfterRestart)
            {
                Validate(job.Request);
                Activate(job.Id, job.Request);
                _rotatorNeedsStop = job.Request.Tracking.PointRotator;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not restore station tracking configuration");
            _status = new TrackingJobStatus { Phase = "restore-error", Error = ex.Message };
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _gate.WaitAsync(stoppingToken);
                try { await TickAsync(stoppingToken); }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (_request is null)
        {
            if (_rotatorNeedsStop)
            {
                try { await StopRotatorAsync(ct); _status.Error = ""; }
                catch (RpcException ex) { _status.Error = ex.Status.Detail; }
            }
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (now < _nextTick) return;
        _nextTick = now + (long)(_request.IntervalMilliseconds / 1000.0 * Stopwatch.Frequency);
        var request = _request.Tracking.Clone();
        request.UtcUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        request.PointRotator = request.PointRotator && _suspendedPointingJob != _status.JobId;
        try
        {
            // A failed rotator reply does not prove no movement occurred.
            var prediction = tracking.Calculate(request);
            ComposeAfc(prediction);
            if (request.PointRotator && prediction.ElevationDegrees >= request.ElevationMaskDegrees)
                _rotatorNeedsStop = true;
            var result = await tracking.ApplyPredictionAsync(request, prediction, ct,
                () => _suspendedPointingJob != _status.JobId);
            _afc?.Readback(result);
            _status.Latest = result;
            _status.CompletedTicks++;
            _status.Error = result.RotatorError;
            _status.Phase = result.CatApplied
                ? (result.RotatorError.Length == 0 ? "tracking" : "device-error")
                : "below-mask";
            if ((!result.CatApplied || !request.PointRotator) && _rotatorNeedsStop) await StopRotatorAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _status.Phase = "device-error";
            _status.Error = ex is RpcException rpc ? rpc.Status.Detail : ex.Message;
            logger.LogWarning(ex, "Tracking tick failed; next tick will recompute current UTC");
        }
        // Never enqueue or replay historical points after a delayed device reply.
        _nextTick = Math.Max(_nextTick, Stopwatch.GetTimestamp());
    }

    private async Task StopRotatorAsync(CancellationToken ct)
    {
        await device.Rotator.StopAsync(new RotatorRead(),
            deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: ct);
        _rotatorNeedsStop = false;
    }
}
