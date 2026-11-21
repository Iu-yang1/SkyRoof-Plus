using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.SgpNet;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

internal static class OrbitTrackingCalculator
{
    private const double SpeedOfLight = 299_792_458.0;

    internal static TrackingSnapshot Predict(TrackingRequest request)
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
        var backend = new SgpNetOrbitBackend();
        var track = backend.CreateTleTrack(request.TleLine1, request.TleLine2);
        var observed = track.Observe(new GroundStationSpec(request.LatitudeDegrees,
            request.LongitudeDegrees, request.HeightMetres), utc);
        var factor = observed.RangeRateMetresPerSecond / SpeedOfLight;
        return new TrackingSnapshot
        {
            NoradNumber = track.NoradNumber,
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
