using System.Diagnostics;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class RotatorService
{
    public override async Task<RotatorPosition> Park(RotatorParkRequest request,
        ServerCallContext context)
    {
        if (request.Waypoints.Count == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Park requires at least one AZ/EL waypoint."));
        foreach (var point in request.Waypoints) ValidatePoint(point);

        double tolerance = request.ToleranceDegrees == 0 ? 1.5 : request.ToleranceDegrees;
        int timeoutSeconds = request.SegmentTimeoutSeconds == 0
            ? 180 : request.SegmentTimeoutSeconds;
        if (!double.IsFinite(tolerance) || tolerance <= 0 || timeoutSeconds <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Park tolerance and segment timeout must be positive."));

        long revision = Interlocked.Increment(ref _motionRevision);
        try
        {
            RotatorPosition? latest = null;
            foreach (var target in request.Waypoints)
            {
                var elapsed = Stopwatch.StartNew();
                int consecutiveArrivals = 0;
                latest = await ExecuteAsync(FormatPoint(target),
                    context.CancellationToken, revision);
                while (true)
                {
                    consecutiveArrivals = AtTarget(latest, target, tolerance)
                        ? consecutiveArrivals + 1 : 0;
                    if (consecutiveArrivals >= 2) break;
                    if (revision != Volatile.Read(ref _motionRevision))
                        throw new RpcException(new Status(StatusCode.Aborted,
                            "Park superseded by newer movement or STOP."));
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
                        throw new RpcException(new Status(StatusCode.DeadlineExceeded,
                            "Park segment did not reach the target before timeout."));
                    await Task.Delay(250, context.CancellationToken);
                    latest = await ExecuteAsync(null, context.CancellationToken, revision);
                }
            }
            return latest!;
        }
        catch (Exception ex) when (ex is RpcException or OperationCanceledException)
        {
            // A timed-out or disconnected park can leave the motor moving.
            // Do not cancel a newer user's Point/STOP while cleaning up.
            if (revision == Volatile.Read(ref _motionRevision))
            {
                Interlocked.Increment(ref _motionRevision);
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await SendStopAsync(stopTimeout.Token); }
                catch (Exception stopError)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable,
                        "Park interrupted; emergency STOP unconfirmed: " + stopError.Message));
                }
            }
            throw;
        }
    }

    private static bool AtTarget(RotatorPosition actual, RotatorPoint requested,
        double tolerance) =>
        actual.HasPosition && actual.ReadbackFresh &&
        Math.Abs(actual.AzimuthDegrees - requested.AzimuthDegrees) <= tolerance &&
        Math.Abs(actual.ElevationDegrees - requested.ElevationDegrees) <= tolerance;
}
