using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkyRoof.Orbit.Orekit;
using org.orekit.time;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: SkyRoof.Orbit.Orekit.Smoke <orekit-data-directory> [report.json]");
    return 2;
}

try
{
    var timer = Stopwatch.StartNew();
    var dataDirectory = OrekitDataContext.Initialize(args[0]);
    var initializedMs = timer.Elapsed.TotalMilliseconds;

    var utc = TimeScalesFactory.getUTC();
    var t2020 = new AbsoluteDate(2020, 1, 1, 0, 0, 0.0, utc);
    double taiMinusUtcSeconds = -utc.offsetFromTAI(t2020).toDouble();
    Assert(Math.Abs(taiMinusUtcSeconds - 37.0) < 1e-9,
        $"UTC/TAI 2020 test failed: {taiMinusUtcSeconds:F6}s, expected 37s");

    // Vallado et al. AIAA-2006-6753, SGP4-VER.TLE / tcppver.out, satellite 00005.
    // Reference vectors are TEME: kilometres and kilometres/second -> SI.
    const string line1 = "1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753";
    const string line2 = "2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667";
    var track = new OrekitTleTrack(line1, line2);
    Assert(track.NoradNumber == 5, "NORAD parsing failed");

    var cases = new[]
    {
        new { Minutes = 0.0, P = new[] {7022.46529266, -1400.08296755, 0.03995155},
              V = new[] {1.893841015, 6.405893759, 4.534807250} },
        new { Minutes = 360.0, P = new[] {-7154.03120202, -3783.17682504, -3536.19412294},
              V = new[] {4.741887409, -4.151817765, -2.093935425} }
    };

    var comparisons = new List<object>();
    foreach (var test in cases)
    {
        var state = track.PredictTemeAtOffset(TimeSpan.FromMinutes(test.Minutes));
        var predictedPosition = new[] {state.XMetres, state.YMetres, state.ZMetres};
        var predictedVelocity = new[] {state.VxMetresPerSecond, state.VyMetresPerSecond, state.VzMetresPerSecond};
        double positionErrorM = Distance(predictedPosition, test.P.Select(x => x * 1000.0).ToArray());
        double velocityErrorMps = Distance(predictedVelocity, test.V.Select(x => x * 1000.0).ToArray());
        Assert(double.IsFinite(positionErrorM) && double.IsFinite(velocityErrorMps),
            $"Invalid TEME vector at t+{test.Minutes}min");
        comparisons.Add(new { test.Minutes, positionErrorM, velocityErrorMps, state });
        Console.WriteLine($"Vallado 00005 t+{test.Minutes:F0}m: Δr={positionErrorM:F6} m; Δv={velocityErrorMps:F9} m/s");
    }

    // The same physical instant represented with two timezone offsets must agree.
    // Keep state/observation tests near the 2000 TLE epoch; the separate 2020 time-scale test
    // is independent of orbit propagation and tests the post-2017 leap-second offset.
    var instantUtc = new DateTimeOffset(2000, 6, 27, 18, 50, 19, TimeSpan.Zero);
    var instantOffset = instantUtc.ToOffset(TimeSpan.FromHours(8));
    var stateUtc = track.PredictTeme(instantUtc);
    var stateOffset = track.PredictTeme(instantOffset);
    Assert(Math.Abs(stateUtc.XMetres - stateOffset.XMetres) < 1e-6,
        "DateTimeOffset UTC normalization failed");

    var observation = track.Observe(
        new GroundStationSpec(28.5, 115.9, 40),
        instantUtc);
    Assert(double.IsFinite(observation.AzimuthDegrees) &&
           double.IsFinite(observation.ElevationDegrees) &&
           double.IsFinite(observation.RangeMetres) &&
           double.IsFinite(observation.RangeRateMetresPerSecond) &&
           observation.RangeMetres > 0 &&
           observation.AzimuthDegrees >= 0 && observation.AzimuthDegrees < 360,
        "ITRF station observation has invalid values");

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 1000; i++)
        _ = track.PredictTeme(instantUtc.AddSeconds(i));
    double thousandPredictionsMs = sw.Elapsed.TotalMilliseconds;

    var assemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name is string name &&
                    (name.Contains("orekit", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("hipparchus", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("ikvm", StringComparison.OrdinalIgnoreCase)))
        .Select(a => a.GetName().Name)
        .OrderBy(a => a)
        .ToArray();

    string leapFile = File.Exists(Path.Combine(dataDirectory, "tai-utc.dat"))
        ? "tai-utc.dat" : "UTC-TAI.history";
    var report = new
    {
        status = "P0_SMOKE_COMPLETED_NOT_ACCURACY_ACCEPTED",
        orekitVersion = "13.1.9",
        ikvmMavenSdkVersion = "1.12.2",
        dataFile = leapFile,
        dataSha256 = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(dataDirectory, leapFile)))).ToLowerInvariant(),
        initialSetupMs = initializedMs,
        thousandPredictionsMs,
        processWorkingSetBytes = Environment.WorkingSet,
        comparisons,
        observation,
        taiMinusUtcSeconds,
        loadedAssemblies = assemblies
    };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (args.Length == 2)
    {
        var path = Path.GetFullPath(args[1]);
        string? parentDirectory = Path.GetDirectoryName(path);
        if (parentDirectory is not null)
            Directory.CreateDirectory(parentDirectory);
        File.WriteAllText(path, json);
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static double Distance(double[] a, double[] b) =>
    Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
