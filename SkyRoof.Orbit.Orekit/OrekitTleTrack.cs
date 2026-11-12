using org.orekit.bodies;
using org.orekit.frames;
using org.orekit.propagation.analytical.tle;
using org.orekit.time;
using org.orekit.utils;

namespace SkyRoof.Orbit.Orekit;

/// <summary>
/// An isolated, reusable Orekit TLE/SGP4 track. P0 is not connected to CAT/rotator/UI.
/// Position/velocity in TEME use metres and metres per second.
/// </summary>
public sealed class OrekitTleTrack
{
    private readonly TLE tle;
    private readonly TLEPropagator propagator;

    public OrekitTleTrack(string line1, string line2)
    {
        // Do not trim/recompose fixed-column TLE data.
        tle = new TLE(line1, line2, TimeScalesFactory.getUTC());
        propagator = TLEPropagator.selectExtrapolator(tle);
    }

    public int NoradNumber => tle.getSatelliteNumber();

    public OrbitState PredictTeme(DateTimeOffset utc) =>
        Propagate(utc.ToUniversalTime(), ToAbsoluteDate(utc));

    public OrbitState PredictTemeAtOffset(TimeSpan offset)
    {
        var date = tle.getDate().shiftedBy(offset.TotalSeconds);
        // The epoch is carried as a UTC timestamp for reporting purposes only.
        // For comparison against Vallado, use offset from TLE epoch directly.
        var epoch = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reportedUtc = epoch.AddSeconds(date.durationFrom(
            new AbsoluteDate(2000, 1, 1, 0, 0, 0.0, TimeScalesFactory.getUTC())));
        return Propagate(reportedUtc, date);
    }

    private OrbitState Propagate(DateTimeOffset utc, AbsoluteDate date)
    {
        // TLEPropagator's native frame is TEME, not GCRF or ITRF.
        var pv = propagator.getPVCoordinates(date);
        var p = pv.getPosition();
        var v = pv.getVelocity();
        return new OrbitState(
            utc, "TEME",
            p.getX(), p.getY(), p.getZ(),
            v.getX(), v.getY(), v.getZ());
    }

    public OrbitObservation Observe(GroundStationSpec ground, DateTimeOffset utc)
    {
        var date = ToAbsoluteDate(utc);
        // P0 uses simpleEOP=true and a UTC-only fixture. This is *not* an
        // EOP-verified precision observation; label the degraded input honestly.
        var itrf = FramesFactory.getITRF(IERSConventions.IERS_2010, true);
        var ellipsoid = new OneAxisEllipsoid(
            Constants.WGS84_EARTH_EQUATORIAL_RADIUS,
            Constants.WGS84_EARTH_FLATTENING,
            itrf);
        var site = new GeodeticPoint(
            ground.LatitudeDegrees * Math.PI / 180.0,
            ground.LongitudeDegrees * Math.PI / 180.0,
            ground.EllipsoidalHeightMetres);
        var local = new TopocentricFrame(ellipsoid, site, "SkyRoof-P0-Station");
        var pv = propagator.getPVCoordinates(date, itrf);
        var position = pv.getPosition();
        return new OrbitObservation(
            utc.ToUniversalTime(),
            local.getAzimuth(position, itrf, date) * 180.0 / Math.PI,
            local.getElevation(position, itrf, date) * 180.0 / Math.PI,
            local.getRange(position, itrf, date),
            local.getRangeRate(pv, itrf, date),
            "ITRF/IERS2010",
            "P0_UTC_ONLY_EOP_UNVERIFIED");
    }

    public static AbsoluteDate ToAbsoluteDate(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        double seconds = utc.Second +
            (utc.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;
        return new AbsoluteDate(
            utc.Year, utc.Month, utc.Day,
            utc.Hour, utc.Minute, seconds, TimeScalesFactory.getUTC());
    }
}
