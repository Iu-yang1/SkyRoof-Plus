using System.Collections.Concurrent;
using org.orekit.bodies;
using org.orekit.frames;
using org.orekit.time;
using org.orekit.utils;
using SkyRoof.Orbit.Contracts;

namespace SkyRoof.Orbit.Orekit;

/// <summary>Orekit planetary JPL DE-series ephemeris in Earth-fixed coordinates.</summary>
public sealed class OrekitCelestialTrack : IOrbitTrack
{
    private readonly CelestialBody body;
    private readonly Frame itrf;
    private readonly OneAxisEllipsoid earth;
    private readonly ConcurrentDictionary<GroundStationSpec, TopocentricFrame> stations = new();

    public OrekitCelestialTrack(string name)
    {
        body = name.ToUpperInvariant() switch
        {
            "MOON" => CelestialBodyFactory.getMoon(),
            "SUN" => CelestialBodyFactory.getSun(),
            "VENUS" => CelestialBodyFactory.getVenus(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unsupported celestial body.")
        };
        itrf = FramesFactory.getITRF(IERSConventions.IERS_2010, true);
        earth = new OneAxisEllipsoid(Constants.WGS84_EARTH_EQUATORIAL_RADIUS,
            Constants.WGS84_EARTH_FLATTENING, itrf);
    }

    public int NoradNumber => 0;

    public OrbitState PredictTeme(DateTimeOffset utc)
    {
        var date = ToAbsoluteDate(utc);
        var pv = body.getPVCoordinates(date, FramesFactory.getTEME());
        var p = pv.getPosition();
        var v = pv.getVelocity();
        return new OrbitState(utc.ToUniversalTime(), "TEME",
            p.getX(), p.getY(), p.getZ(), v.getX(), v.getY(), v.getZ());
    }

    public OrbitGeodetic PredictGeodetic(DateTimeOffset utc) =>
        throw new NotSupportedException("Celestial bodies do not have satellite geodetic sub-points.");

    public OrbitObservation Observe(GroundStationSpec ground, DateTimeOffset utc)
    {
        var date = ToAbsoluteDate(utc);
        var site = stations.GetOrAdd(ground, spec =>
            new TopocentricFrame(earth,
                new GeodeticPoint(spec.LatitudeDegrees * Math.PI / 180.0,
                    spec.LongitudeDegrees * Math.PI / 180.0,
                    spec.EllipsoidalHeightMetres),
                $"SkyRoof-{spec.LatitudeDegrees:F6}-{spec.LongitudeDegrees:F6}"));
        var pv = body.getPVCoordinates(date, itrf);
        return new OrbitObservation(utc.ToUniversalTime(),
            site.getAzimuth(pv.getPosition(), itrf, date) * 180.0 / Math.PI,
            site.getElevation(pv.getPosition(), itrf, date) * 180.0 / Math.PI,
            site.getRange(pv.getPosition(), itrf, date),
            site.getRangeRate(pv, itrf, date),
            "ITRF/IERS2010/DE430",
            OrekitDataContext.DataQuality(utc));
    }

    private static AbsoluteDate ToAbsoluteDate(DateTimeOffset instant)
    {
        DateTime utc = instant.UtcDateTime;
        double seconds = utc.Second +
            (utc.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;
        return new AbsoluteDate(utc.Year, utc.Month, utc.Day,
            utc.Hour, utc.Minute, seconds, TimeScalesFactory.getUTC());
    }
}
