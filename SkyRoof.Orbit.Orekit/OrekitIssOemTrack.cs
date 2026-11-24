using System.Collections.Concurrent;
using org.orekit.bodies;
using org.orekit.data;
using org.orekit.files.ccsds.ndm;
using org.orekit.files.ccsds.ndm.odm.oem;
using org.orekit.frames;
using org.orekit.files.general;
using org.orekit.propagation;
using org.orekit.time;
using org.orekit.utils;
using SkyRoof.Orbit.Contracts;

namespace SkyRoof.Orbit.Orekit;

/// <summary>ISS CCSDS OEM (KVN or XML); no extrapolation across segment boundaries.</summary>
public sealed class OrekitIssOemTrack : IOrbitTrack
{
    private readonly record struct Span(AbsoluteDate Start, AbsoluteDate Stop, BoundedPropagator Propagator);
    private readonly Span[] segments;
    private readonly Frame itrf = FramesFactory.getITRF(IERSConventions.IERS_2010, true);
    private readonly OneAxisEllipsoid earth;
    private readonly ConcurrentDictionary<GroundStationSpec, TopocentricFrame> stations = new();
    private readonly object sync = new();

    public OrekitIssOemTrack(string path)
    {
        var oem = (Oem)new ParserBuilder().withMu(Constants.WGS84_EARTH_MU)
            .buildOemParser().parseMessage(new DataSource(Path.GetFullPath(path)));
        var list = oem.getSegments();
        var spans = new List<Span>();
        for (int i = 0; i < list.size(); i++)
        {
            var segment = (OemSegment)list.get(i);
            var metadata = (OemMetadata)segment.getMetadata();
            if (metadata.getObjectID().Replace("-", "", StringComparison.Ordinal) != "1998067A")
                continue;
            if (!string.Equals(metadata.getCenter().getName(), "EARTH", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ISS OEM must be Earth-centred.");
            if (segment.getCoordinates().size() < segment.getInterpolationSamples())
                throw new InvalidDataException("ISS OEM segment lacks interpolation samples.");
            spans.Add(new Span(segment.getStart(), segment.getStop(), ((EphemerisFile.EphemerisSegment)segment).getPropagator()));
        }
        if (spans.Count == 0)
            throw new InvalidDataException("OEM does not contain ISS object 1998-067A.");
        segments = spans.ToArray();
        earth = new OneAxisEllipsoid(Constants.WGS84_EARTH_EQUATORIAL_RADIUS,
            Constants.WGS84_EARTH_FLATTENING, itrf);
    }

    public int NoradNumber => 25544;

    public bool Covers(DateTimeOffset instant)
    {
        var date = ToDate(instant);
        return segments.Any(segment => Contains(segment, date));
    }

    private static bool Contains(Span segment, AbsoluteDate date) =>
        date.durationFrom(segment.Start) >= 0 && date.durationFrom(segment.Stop) <= 0;

    private PVCoordinates Coordinates(AbsoluteDate date, Frame frame)
    {
        // Newer segments take precedence at shared or overlapping endpoints.
        lock (sync)
        {
            foreach (var segment in segments.Reverse())
                if (Contains(segment, date))
                    return segment.Propagator.getPVCoordinates(date, frame);
        }
        throw new ArgumentOutOfRangeException(nameof(date), "Instant outside ISS OEM usable segments.");
    }

    public OrbitState PredictTeme(DateTimeOffset utc)
    {
        var pv = Coordinates(ToDate(utc), FramesFactory.getTEME());
        var p = pv.getPosition();
        var v = pv.getVelocity();
        return new OrbitState(utc.ToUniversalTime(), "TEME",
            p.getX(), p.getY(), p.getZ(), v.getX(), v.getY(), v.getZ());
    }

    public OrbitGeodetic PredictGeodetic(DateTimeOffset utc)
    {
        var date = ToDate(utc);
        var point = earth.transform(Coordinates(date, itrf).getPosition(), itrf, date);
        return new OrbitGeodetic(utc.ToUniversalTime(), point.getLatitude() * 180 / Math.PI,
            point.getLongitude() * 180 / Math.PI, point.getAltitude());
    }

    public OrbitObservation Observe(GroundStationSpec ground, DateTimeOffset utc)
    {
        var date = ToDate(utc);
        var site = stations.GetOrAdd(ground, spec => new TopocentricFrame(earth,
            new GeodeticPoint(spec.LatitudeDegrees * Math.PI / 180,
                spec.LongitudeDegrees * Math.PI / 180, spec.EllipsoidalHeightMetres), "SkyRoof-ISS"));
        var pv = Coordinates(date, itrf);
        var p = pv.getPosition();
        return new OrbitObservation(utc.ToUniversalTime(),
            site.getAzimuth(p, itrf, date) * 180 / Math.PI,
            site.getElevation(p, itrf, date) * 180 / Math.PI,
            site.getRange(p, itrf, date), site.getRangeRate(pv, itrf, date),
            "ITRF/IERS2010", "ISS_OEM;" + OrekitDataContext.DataQuality(utc));
    }

    private static AbsoluteDate ToDate(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        return new AbsoluteDate(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute,
            utc.Second + (utc.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond,
            TimeScalesFactory.getUTC());
    }
}
