using SkyRoof.Orbit.Contracts;
using System.Collections.Concurrent;
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
public sealed class OrekitTleTrack : IOrbitTrack
{
    private readonly TLE tle;
    private readonly TLEPropagator propagator;
    private readonly object propagateSync = new();
    private readonly Frame itrf;
    private readonly OneAxisEllipsoid ellipsoid;
    private readonly ConcurrentDictionary<GroundStationSpec, TopocentricFrame> siteFrames = new();

    public OrekitTleTrack(string line1, string line2)
    {
        // Do not trim/recompose fixed-column TLE data.
        tle = new TLE(line1, line2, TimeScalesFactory.getUTC());
        propagator = TLEPropagator.selectExtrapolator(tle);
        itrf = FramesFactory.getITRF(IERSConventions.IERS_2010, true);
        ellipsoid = new OneAxisEllipsoid(
            Constants.WGS84_EARTH_EQUATORIAL_RADIUS,
            Constants.WGS84_EARTH_FLATTENING, itrf);
    }

    public OrekitTleTrack(OrbitMeanElements elements)
    {
        var utc = TimeScalesFactory.getUTC();
        const double secondsPerDay = 86400.0;
        const double twoPi = 2 * Math.PI;
        int launchYear = 0;
        int launchNumber = 0;
        string launchPiece = "A";
        string id = elements.ObjectId;
        if (id.Length >= 8 && id[4] == '-' &&
            int.TryParse(id.AsSpan(0, 4), out int year) &&
            int.TryParse(id.AsSpan(5, 3), out int number))
        {
            launchYear = year;
            launchNumber = number;
            if (id.Length > 8)
                launchPiece = id[8..];
        }

        tle = new TLE(
            elements.NoradNumber, elements.Classification,
            launchYear, launchNumber, launchPiece,
            elements.EphemerisType, elements.ElementSetNumber,
            ToAbsoluteDate(elements.EpochUtc),
            elements.MeanMotionRevolutionsPerDay * twoPi / secondsPerDay,
            elements.MeanMotionDerivativeRevolutionsPerDay2 * twoPi / (secondsPerDay * secondsPerDay),
            elements.MeanMotionSecondDerivativeRevolutionsPerDay3 * twoPi / (secondsPerDay * secondsPerDay * secondsPerDay),
            elements.Eccentricity, elements.InclinationDegrees * Math.PI / 180.0,
            elements.ArgumentOfPerigeeDegrees * Math.PI / 180.0,
            elements.RightAscensionDegrees * Math.PI / 180.0,
            elements.MeanAnomalyDegrees * Math.PI / 180.0,
            elements.RevolutionNumberAtEpoch, elements.BStar, utc);
        propagator = TLEPropagator.selectExtrapolator(tle);
        itrf = FramesFactory.getITRF(IERSConventions.IERS_2010, true);
        ellipsoid = new OneAxisEllipsoid(
            Constants.WGS84_EARTH_EQUATORIAL_RADIUS,
            Constants.WGS84_EARTH_FLATTENING, itrf);
    }

    public int NoradNumber => tle.getSatelliteNumber();

    public OrbitState PredictTeme(DateTimeOffset utc) =>
        Propagate(utc.ToUniversalTime(), ToAbsoluteDate(utc));

    public OrbitState PredictTemeAtOffset(TimeSpan offset)
    {
        var date = tle.getDate().shiftedBy(offset.TotalSeconds);
        return Propagate(ToUtcDateTimeOffset(date), date);
    }

    private OrbitState Propagate(DateTimeOffset utc, AbsoluteDate date)
    {
        // TLEPropagator's native frame is TEME, not GCRF or ITRF.
        org.orekit.utils.PVCoordinates pv;
        lock (propagateSync)
            pv = propagator.getPVCoordinates(date);
        var p = pv.getPosition();
        var v = pv.getVelocity();
        return new OrbitState(
            utc, "TEME",
            p.getX(), p.getY(), p.getZ(),
            v.getX(), v.getY(), v.getZ());
    }

    public OrbitGeodetic PredictGeodetic(DateTimeOffset utc)
    {
        var date = ToAbsoluteDate(utc);
        org.orekit.utils.PVCoordinates pv;
        lock (propagateSync)
            pv = propagator.getPVCoordinates(date, itrf);
        var point = ellipsoid.transform(pv.getPosition(), itrf, date);
        return new OrbitGeodetic(
            utc.ToUniversalTime(),
            point.getLatitude() * 180 / Math.PI,
            point.getLongitude() * 180 / Math.PI,
            point.getAltitude());
    }

    public OrbitObservation Observe(GroundStationSpec ground, DateTimeOffset utc)
    {
        var date = ToAbsoluteDate(utc);
        var local = siteFrames.GetOrAdd(ground, spec =>
            new TopocentricFrame(
                ellipsoid,
                new GeodeticPoint(
                    spec.LatitudeDegrees * Math.PI / 180.0,
                    spec.LongitudeDegrees * Math.PI / 180.0,
                    spec.EllipsoidalHeightMetres),
                $"SkyRoof-{spec.LatitudeDegrees:F6}-{spec.LongitudeDegrees:F6}"));
        org.orekit.utils.PVCoordinates pv;
        lock (propagateSync)
            pv = propagator.getPVCoordinates(date, itrf);
        var position = pv.getPosition();
        return new OrbitObservation(
            utc.ToUniversalTime(),
            local.getAzimuth(position, itrf, date) * 180.0 / Math.PI,
            local.getElevation(position, itrf, date) * 180.0 / Math.PI,
            local.getRange(position, itrf, date),
            local.getRangeRate(pv, itrf, date),
            "ITRF/IERS2010",
            OrekitDataContext.DataQuality(utc));
    }

    private static DateTimeOffset ToUtcDateTimeOffset(AbsoluteDate date)
    {
        var parts = date.getComponents(TimeScalesFactory.getUTC());
        var day = parts.getDate();
        var clock = parts.getTime();
        // DateTimeOffset has no representation for 23:59:60; keep the
        // physically correct AbsoluteDate internally and report that case
        // explicitly rather than silently identifying the leap second as
        // an adjacent civil second.
        if (clock.getSecond() >= 60.0)
            throw new NotSupportedException(
                "DateTimeOffset cannot represent the UTC leap second; use an explicit UTC instant.");
        var minute = new DateTimeOffset(
            day.getYear(), day.getMonth(), day.getDay(),
            clock.getHour(), clock.getMinute(), 0, TimeSpan.Zero);
        return minute.AddTicks((long)Math.Round(clock.getSecond() * TimeSpan.TicksPerSecond));
    }

    private static AbsoluteDate ToAbsoluteDate(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        double seconds = utc.Second +
            (utc.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;
        return new AbsoluteDate(
            utc.Year, utc.Month, utc.Day,
            utc.Hour, utc.Minute, seconds, TimeScalesFactory.getUTC());
    }
}
