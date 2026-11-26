using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class TrackingJobRunner
{
    private TrackingJobStatus Snapshot()
    {
        var result = _status.Clone();
        if (_request?.Tracking is not { } current) return result;
        result.CelestialTarget = current.CelestialTarget;
        result.PointRotator = current.PointRotator && _suspendedPointingJob != _status.JobId;
        if (current.CelestialTarget.Length == 0)
            result.ActiveRadioLink = current.RadioLink?.Clone() ?? new RadioLinkProfile
            {
                DownlinkLowHz = current.DownlinkHz, UplinkLowHz = current.UplinkHz
            };
        return result;
    }
}
