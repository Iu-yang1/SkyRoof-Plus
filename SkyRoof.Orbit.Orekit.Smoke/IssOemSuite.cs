using org.hipparchus.geometry.euclidean.threed;
using org.orekit.frames;
using org.orekit.time;
using org.orekit.utils;
using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.Orekit;

internal static class IssOemSuite
{
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".oem");
        try
        {
            File.WriteAllText(path, Fixture);
            var track = new OrekitIssOemTrack(path);
            var start = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
            var midpoint = start.AddSeconds(60);
            if (track.NoradNumber != 25544 || !track.Covers(start) || !track.Covers(midpoint) ||
                track.Covers(start.AddMinutes(3)) || track.Covers(start.AddSeconds(-1)) ||
                !track.Covers(start.AddMinutes(5)))
                throw new InvalidOperationException("ISS OEM coverage/gap selection failed.");

            // Independent source-frame PV (linear synthetic trajectory), transformed by Orekit.
            var date = new AbsoluteDate(2026, 10, 8, 0, 1, 0.0, TimeScalesFactory.getUTC());
            var expected = FramesFactory.getEME2000().getTransformTo(FramesFactory.getTEME(), date)
                .transformPVCoordinates(new PVCoordinates(new Vector3D(7000000, 450000, 0),
                    new Vector3D(0, 7500, 0)));
            var result = track.PredictTeme(midpoint);
            var actual = new Vector3D(result.XMetres, result.YMetres, result.ZMetres);
            if (Vector3D.distance(actual, expected.getPosition()) > 0.1)
                throw new InvalidOperationException("ISS OEM source-frame interpolation or units failed.");
            var ground = new GroundStationSpec(28.5, 115.9, 40);
            double rangeRate = (track.Observe(ground, midpoint.AddSeconds(1)).RangeMetres -
                track.Observe(ground, midpoint.AddSeconds(-1)).RangeMetres) / 2;
            if (Math.Abs(rangeRate - track.Observe(ground, midpoint).RangeRateMetresPerSecond) > 3)
                throw new InvalidOperationException("ISS OEM topocentric Doppler sign/range-rate failed.");
            Console.WriteLine("PASS: synthetic ISS OEM EME2000 PV interpolation, nonuniform segment interpolation, gap, frame and range-rate");
        }
        finally { File.Delete(path); }
    }

    internal static string Fixture => Header + Segment("00:00", "00:02", 0) + Segment("00:04", "00:06", 240);

    private const string Header = """
        CCSDS_OEM_VERS = 2.0
        CREATION_DATE = 2026-10-08T00:00:00
        ORIGINATOR = SKYROOF_SYNTHETIC_TEST

        """ + "\n";

    private static string Segment(string start, string stop, int offset)
    {
        var begin = DateTime.ParseExact(start, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        string Row(int seconds) =>
            FormattableString.Invariant($"2026-10-08T{begin.AddSeconds(seconds):HH:mm:ss} 7000 {(offset + seconds) * 7.5:F3} 0 0 7.5 0");
        return $"""
            META_START
            OBJECT_NAME = ISS
            OBJECT_ID = 1998-067A
            CENTER_NAME = EARTH
            REF_FRAME = EME2000
            TIME_SYSTEM = UTC
            START_TIME = 2026-10-08T{start}:00
            STOP_TIME = 2026-10-08T{stop}:00
            INTERPOLATION = HERMITE
            INTERPOLATION_DEGREE = 2
            META_STOP
            {Row(0)}
            {Row(45)}
            {Row(120)}

            """ + "\n";
    }
}
