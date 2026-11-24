using System.Globalization;
using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.Orekit;
using org.hipparchus.geometry.euclidean.threed;
using org.orekit.frames;
using org.orekit.time;

internal static class IssOemLiveSuite
{
    internal static void Run(string path)
    {
        var track = new OrekitIssOemTrack(path);
        int tested = 0;
        var site = new GroundStationSpec(28.5, 115.9, 40);
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 7 || !DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var utc) || !track.Covers(utc)) continue;
            var values = fields.Skip(1).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            var date = new AbsoluteDate(utc.UtcDateTime.Year, utc.UtcDateTime.Month, utc.UtcDateTime.Day,
                utc.Hour, utc.Minute, utc.Second + utc.Millisecond / 1000.0, TimeScalesFactory.getUTC());
            var expected = FramesFactory.getEME2000().getTransformTo(FramesFactory.getTEME(), date)
                .transformPosition(new Vector3D(values[0] * 1000, values[1] * 1000, values[2] * 1000));
            var actual = track.PredictTeme(utc);
            double error = Vector3D.distance(expected, new Vector3D(actual.XMetres, actual.YMetres, actual.ZMetres));
            if (error > 2 || !double.IsFinite(error))
                throw new InvalidDataException($"NASA source knot mismatch: {error:F3} m at {utc:O}.");
            if (track.Covers(utc.AddSeconds(-1)) && track.Covers(utc.AddSeconds(1)))
            {
                var observed = track.Observe(site, utc);
                double derivative = (track.Observe(site, utc.AddSeconds(1)).RangeMetres -
                    track.Observe(site, utc.AddSeconds(-1)).RangeMetres) / 2;
                if (!double.IsFinite(derivative) || Math.Abs(derivative - observed.RangeRateMetresPerSecond) > 5)
                    throw new InvalidDataException("NASA OEM range rate disagrees with finite-difference geometry.");
            }
            if (++tested == 50) break;
        }
        if (tested < 10) throw new InvalidDataException("NASA OEM did not contain enough usable source vectors.");
        Console.WriteLine($"PASS: NASA OEM {tested} actual source knots and topocentric range-rate checks. Current coverage={track.Covers(DateTimeOffset.UtcNow)}");
    }
}
