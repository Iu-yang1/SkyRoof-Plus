using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class OrbitTrackingCalculator(IOrbitBackend backend)
{
    private const double SpeedOfLight = 299_792_458.0;

    private readonly object _sync = new();
    private string? _line1, _line2;
    private IOrbitTrack? _track;

    internal TrackingSnapshot Predict(TrackingRequest request)
    {
        if (!double.IsFinite(request.LatitudeDegrees) ||
            request.LatitudeDegrees is < -90 or > 90 ||
            !double.IsFinite(request.LongitudeDegrees) ||
            request.LongitudeDegrees is < -180 or > 180 ||
            !double.IsFinite(request.HeightMetres) ||
            !double.IsFinite(request.ElevationMaskDegrees) ||
            request.DownlinkHz <= 0 || request.UplinkHz < 0)
            throw new ArgumentException("Invalid ground site or tracking frequencies.");

        var utc = DateTimeOffset.FromUnixTimeMilliseconds(request.UtcUnixMilliseconds);
        OrbitObservation observed;
        int noradNumber;
        lock (_sync)
        {
            if (_track is null || _line1 != request.TleLine1 || _line2 != request.TleLine2)
            {
                _track = backend.CreateTleTrack(request.TleLine1, request.TleLine2);
                _line1 = request.TleLine1;
                _line2 = request.TleLine2;
            }
            noradNumber = _track.NoradNumber;
            observed = _track.Observe(new GroundStationSpec(request.LatitudeDegrees,
                request.LongitudeDegrees, request.HeightMetres), utc);
        }
        var factor = observed.RangeRateMetresPerSecond / SpeedOfLight;
        return new TrackingSnapshot
        {
            NoradNumber = noradNumber,
            UtcUnixMilliseconds = utc.ToUnixTimeMilliseconds(),
            AzimuthDegrees = observed.AzimuthDegrees,
            ElevationDegrees = observed.ElevationDegrees,
            RangeMetres = observed.RangeMetres,
            RangeRateMetresPerSecond = observed.RangeRateMetresPerSecond,
            RxHz = Correct(request.DownlinkHz, request.CorrectDownlink ? -factor : 0),
            TxHz = Correct(request.UplinkHz, request.CorrectUplink ? factor : 0),
            OrbitBackend = backend.BackendId,
            DataQuality = observed.DataQuality
        };
    }

    // Preserve RadioLink's first-order sign convention: positive range rate =
    // receding, so RX frequency falls and commanded uplink frequency rises.
    private static long Correct(long frequency, double signedFactor) =>
        checked((long)Math.Round(frequency * (1 + signedFactor)));
}
