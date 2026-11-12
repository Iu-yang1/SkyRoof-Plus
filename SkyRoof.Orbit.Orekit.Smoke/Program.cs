using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkyRoof.Orbit.Orekit;
using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.SgpNet;
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
    IOrbitBackend backend = new OrekitOrbitBackend();
    IOrbitTrack interfaceTrack = backend.CreateTleTrack(line1, line2);
    Assert(backend.BackendId == "orekit-ikvm" && interfaceTrack.NoradNumber == 5,
        "P1 contract/backend connection failed");
    IOrbitBackend baselineBackend = new SgpNetOrbitBackend();
    IOrbitTrack sgpNetTrack = baselineBackend.CreateTleTrack(line1, line2);
    Assert(baselineBackend.BackendId == "sgpnet" && sgpNetTrack.NoradNumber == 5,
        "SGP.NET reference adapter failed");
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
        Assert(positionErrorM <= 0.1 && velocityErrorMps <= 0.001,
            $"Vallado TEME mismatch at t+{test.Minutes}min: {positionErrorM}m, {velocityErrorMps}m/s");
        var baselineState = sgpNetTrack.PredictTeme(state.Utc);
        var baselineP = new[] { baselineState.XMetres, baselineState.YMetres, baselineState.ZMetres };
        var baselineV = new[] { baselineState.VxMetresPerSecond, baselineState.VyMetresPerSecond, baselineState.VzMetresPerSecond };
        double sgpNetReferencePositionErrorM = Distance(baselineP, test.P.Select(x => x * 1000.0).ToArray());
        double sgpNetReferenceVelocityErrorMps = Distance(baselineV, test.V.Select(x => x * 1000.0).ToArray());
        double orekitSgpPositionDifferenceM = Distance(predictedPosition, baselineP);
        double orekitSgpVelocityDifferenceMps = Distance(predictedVelocity, baselineV);
        Assert(double.IsFinite(sgpNetReferencePositionErrorM) &&
               double.IsFinite(sgpNetReferenceVelocityErrorMps) &&
               double.IsFinite(orekitSgpPositionDifferenceM) &&
               double.IsFinite(orekitSgpVelocityDifferenceMps),
            $"Invalid SGP.NET reference comparison at t+{test.Minutes}min");
        comparisons.Add(new {
            test.Minutes,
            orekitReferencePositionErrorM = positionErrorM,
            orekitReferenceVelocityErrorMps = velocityErrorMps,
            sgpNetReferencePositionErrorM,
            sgpNetReferenceVelocityErrorMps,
            orekitSgpPositionDifferenceM,
            orekitSgpVelocityDifferenceMps,
            state
        });
        Console.WriteLine($"Vallado 00005 t+{test.Minutes:F0}m: Δr={positionErrorM:F6} m; Δv={velocityErrorMps:F9} m/s");
    }

    // A six-digit NORAD identifier must remain an integer; do not re-encode
    // CCSDS OMM as a lossy fixed-width 69-column TLE.
    var omm = new OrbitMeanElements(
        100001, "2026-001A", 'U',
        new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
        15.5, 0.00001, 0.0,
        0.001, 51.6, 120.0, 80.0, 25.0, 0.0001, 0, 999, 12345);
    var orekitOmm = backend.CreateOmmTrack(omm);
    var sgpOmm = baselineBackend.CreateOmmTrack(omm);
    Assert(orekitOmm.NoradNumber == 100001 && sgpOmm.NoradNumber == 100001,
        "OMM six-digit NORAD ID changed");
    var ommAt = omm.EpochUtc.AddMinutes(10);
    var orekitOmmPv = orekitOmm.PredictTeme(ommAt);
    var sgpOmmPv = sgpOmm.PredictTeme(ommAt);
    var sixDigitErrorMetres = Distance(
        new[] { orekitOmmPv.XMetres, orekitOmmPv.YMetres, orekitOmmPv.ZMetres },
        new[] { sgpOmmPv.XMetres, sgpOmmPv.YMetres, sgpOmmPv.ZMetres });
    Assert(double.IsFinite(sixDigitErrorMetres) && sixDigitErrorMetres < 2000.0,
        $"OMM six-digit SGP4 disagreement: {sixDigitErrorMetres} m");
    Console.WriteLine($"OMM NORAD 100001: Orekit / SGP.NET TEME Δr={sixDigitErrorMetres:F3} m");

    // The same physical instant represented with two timezone offsets must agree.
    // Keep state/observation tests near the 2000 TLE epoch; the separate 2020 time-scale test
    // is independent of orbit propagation and tests the post-2017 leap-second offset.
    var instantUtc = new DateTimeOffset(2000, 6, 27, 18, 50, 19, TimeSpan.Zero);
    var instantOffset = instantUtc.ToOffset(TimeSpan.FromHours(8));
    var stateUtc = interfaceTrack.PredictTeme(instantUtc);
    var stateOffset = track.PredictTeme(instantOffset);
    Assert(Math.Abs(stateUtc.XMetres - stateOffset.XMetres) < 1e-6,
        "DateTimeOffset UTC normalization failed");

    var observation = interfaceTrack.Observe(
        new GroundStationSpec(28.5, 115.9, 40),
        instantUtc);
    var geodetic = interfaceTrack.PredictGeodetic(instantUtc);
    var baselineGeodetic = sgpNetTrack.PredictGeodetic(instantUtc);
    Assert(double.IsFinite(geodetic.LatitudeDegrees) &&
           double.IsFinite(geodetic.LongitudeDegrees) &&
           double.IsFinite(geodetic.EllipsoidalHeightMetres),
        "Orekit geodetic position is invalid");

    var baselineObservation = sgpNetTrack.Observe(
        new GroundStationSpec(28.5, 115.9, 40), instantUtc);
    Assert(double.IsFinite(baselineObservation.RangeMetres) &&
           double.IsFinite(baselineObservation.RangeRateMetresPerSecond),
        "SGP.NET station observation has invalid values");
    Assert(double.IsFinite(observation.AzimuthDegrees) &&
           double.IsFinite(observation.ElevationDegrees) &&
           double.IsFinite(observation.RangeMetres) &&
           double.IsFinite(observation.RangeRateMetresPerSecond) &&
           observation.RangeMetres > 0 &&
           observation.AzimuthDegrees >= 0 && observation.AzimuthDegrees < 360,
        "ITRF station observation has invalid values");

    // Check radial velocity against a centred numerical range derivative.
    // This independently catches mixed frames, metres/kilometres and Doppler sign.
    var station = new GroundStationSpec(28.5, 115.9, 40);
    double orekitFiniteRangeRate =
        (interfaceTrack.Observe(station, instantUtc.AddSeconds(1)).RangeMetres -
         interfaceTrack.Observe(station, instantUtc.AddSeconds(-1)).RangeMetres) / 2.0;
    double sgpNetFiniteRangeRate =
        (sgpNetTrack.Observe(station, instantUtc.AddSeconds(1)).RangeMetres -
         sgpNetTrack.Observe(station, instantUtc.AddSeconds(-1)).RangeMetres) / 2.0;
    Assert(Math.Abs(observation.RangeRateMetresPerSecond - orekitFiniteRangeRate) <= 3.0,
        "Orekit range-rate sign or frame convention differs from range derivative");
    double sgpNetRangeRateResidualMps =
        baselineObservation.RangeRateMetresPerSecond - sgpNetFiniteRangeRate;
    // SGP.NET is the *existing* production baseline. Report its independent
    // consistency residual separately; do not disguise it as Orekit precision.
    // A baseline mismatch is not grounds to disable the new, verified backend.
    Console.WriteLine(
        $"SGP.NET radial velocity: analytical={baselineObservation.RangeRateMetresPerSecond:F6} m/s, " +
        $"d(range)/dt={sgpNetFiniteRangeRate:F6} m/s, residual={sgpNetRangeRateResidualMps:F6} m/s");

    // Analytic 90-minute orbit: search begins while already visible and crosses
    // the next orbit's AOS/LOS. Both physical crossings must be recovered.
    var syntheticEpoch = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    double AnalyticElevation(DateTimeOffset t) =>
        60 * Math.Sin(2 * Math.PI * (t - syntheticEpoch).TotalSeconds / 5400.0);
    var analyticPasses = OrbitPassPredictor.FindPasses(
        AnalyticElevation,
        syntheticEpoch.AddMinutes(10),
        syntheticEpoch.AddMinutes(120),
        sampleStep: TimeSpan.FromSeconds(30));
    Assert(analyticPasses.Count == 2 &&
           analyticPasses.All(pass => pass.ActualAos && pass.ActualLos),
        "AOS/LOS edge recovery or adjacent-pass detection failed");
    Assert(Math.Abs((analyticPasses[0].StartUtc - syntheticEpoch).TotalSeconds) <= 1.0 &&
           Math.Abs((analyticPasses[0].EndUtc - syntheticEpoch.AddMinutes(45)).TotalSeconds) <= 1.0 &&
           Math.Abs((analyticPasses[1].StartUtc - syntheticEpoch.AddMinutes(90)).TotalSeconds) <= 1.0 &&
           Math.Abs((analyticPasses[1].EndUtc - syntheticEpoch.AddMinutes(135)).TotalSeconds) <= 1.0,
        "Analytic pass crossing times differ by more than one second");
    Assert(Math.Abs(analyticPasses[0].MaximumElevationDegrees - 60) < 0.01,
        "Analytic culmination error");

    // A permanently visible object has no physical AOS/LOS in the bounded
    // interval. Mark clipped ends instead of fabricating horizon crossing times.
    var unboundedPass = OrbitPassPredictor.FindPasses(
        _ => 20.0, syntheticEpoch, syntheticEpoch.AddHours(2),
        sampleStep: TimeSpan.FromMinutes(10));
    Assert(unboundedPass.Count == 1 &&
           !unboundedPass[0].ActualAos && !unboundedPass[0].ActualLos,
        "Continuously visible target incorrectly acquired fake AOS/LOS");

    var orbitPasses = interfaceTrack.FindPasses(
        new GroundStationSpec(28.5, 115.9, 40),
        instantUtc, instantUtc.AddHours(4));
    Assert(orbitPasses.All(p => p.EndUtc > p.StartUtc),
        "Orekit observation-driven pass scan returned invalid intervals");

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
    string dataSha256 = Convert.ToHexString(SHA256.HashData(
        File.ReadAllBytes(Path.Combine(dataDirectory, leapFile)))).ToLowerInvariant();
    const string expectedDataSha256 =
        "96017d361fda96801547dc7846c25d4a529ad48128a2aa849962966ad5b95eeb";
    Assert(dataSha256 == expectedDataSha256,
        $"Unexpected UTC fixture SHA-256: {dataSha256}");

    string? eopSha256 = File.Exists(Path.Combine(dataDirectory, "finals2000A.all"))
        ? Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(dataDirectory, "finals2000A.all")))).ToLowerInvariant()
        : null;

    ValladoReferenceReport allVallado = ValladoReferenceSuite.Run(
        args[0], backend, baselineBackend);

    Assert(backend is ICelestialOrbitBackend, "Orekit celestial backend contract unavailable");
    Assert(File.Exists(Path.Combine(dataDirectory, "lnxp1990.430")),
        "The pinned Orekit-native DE430 ephemeris fixture is missing");
    var celestialBackend = (ICelestialOrbitBackend)backend;
    var celestialEpoch = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    var celestialGround = new GroundStationSpec(28.5, 115.9, 40);
    var moon = celestialBackend.CreateCelestialTrack("Moon");
    var sun = celestialBackend.CreateCelestialTrack("Sun");
    var venus = celestialBackend.CreateCelestialTrack("Venus");
    var moonObservation = moon.Observe(celestialGround, celestialEpoch);
    var sunObservation = sun.Observe(celestialGround, celestialEpoch);
    var venusObservation = venus.Observe(celestialGround, celestialEpoch);
    foreach (var item in new[] { moonObservation, sunObservation, venusObservation })
        Assert(double.IsFinite(item.AzimuthDegrees) && item.AzimuthDegrees >= 0 &&
               item.AzimuthDegrees < 360 && double.IsFinite(item.ElevationDegrees) &&
               double.IsFinite(item.RangeMetres) && double.IsFinite(item.RangeRateMetresPerSecond),
            "Orekit celestial topocentric result is not finite");
    Assert(moonObservation.RangeMetres > 3.0e8 && moonObservation.RangeMetres < 4.5e8,
        "Orekit lunar range outside plausible Earth-Moon bounds");
    Assert(sunObservation.RangeMetres > 1.35e11 && sunObservation.RangeMetres < 1.6e11,
        "Orekit solar range outside plausible Earth-Sun bounds");
    Assert(venusObservation.RangeMetres > 3e10 && venusObservation.RangeMetres < 3e11,
        "Orekit Venus range outside broad plausible bounds");
    double moonFiniteRangeRate = (moon.Observe(celestialGround, celestialEpoch.AddSeconds(1)).RangeMetres -
        moon.Observe(celestialGround, celestialEpoch.AddSeconds(-1)).RangeMetres) / 2.0;
    Assert(Math.Abs(moonObservation.RangeRateMetresPerSecond - moonFiniteRangeRate) < 20,
        "Orekit lunar range rate differs from finite-difference range");
    double lunarDopplerHzAt432080 = -432_080_000.0 *
        moonObservation.RangeRateMetresPerSecond / 299_792_458.0;
    Assert(Math.Sign(lunarDopplerHzAt432080) == -Math.Sign(moonObservation.RangeRateMetresPerSecond) ||
           Math.Abs(moonObservation.RangeRateMetresPerSecond) < 1e-9,
        "Lunar one-way downlink Doppler has the wrong sign");
    Console.WriteLine($"Orekit DE430 Moon: range={moonObservation.RangeMetres:F0} m, " +
        $"range rate={moonObservation.RangeRateMetresPerSecond:F3} m/s, " +
        $"Doppler at 432.080 MHz={lunarDopplerHzAt432080:F3} Hz");

    // Measure representative warmed orbit + station observation loops, keeping
    // IKVM JIT startup separate from steady-state timing. Medians reduce CI noise.
    var perfTimes = Enumerable.Range(0, 1000)
        .Select(i => instantUtc.AddSeconds(i)).ToArray();
    var moonPerfTimes = Enumerable.Range(0, 200)
        .Select(i => celestialEpoch.AddSeconds(i * 10)).ToArray();
    double orekitTeme1000MedianMs = MedianMilliseconds(() =>
    {
        foreach (var t in perfTimes) _ = interfaceTrack.PredictTeme(t);
    });
    double sgpNetTeme1000MedianMs = MedianMilliseconds(() =>
    {
        foreach (var t in perfTimes) _ = sgpNetTrack.PredictTeme(t);
    });
    double orekitObserve1000MedianMs = MedianMilliseconds(() =>
    {
        foreach (var t in perfTimes) _ = interfaceTrack.Observe(station, t);
    });
    double moonObserve200MedianMs = MedianMilliseconds(() =>
    {
        foreach (var t in moonPerfTimes) _ = moon.Observe(celestialGround, t);
    });

    // These are broad throughput sanity checks, not a claim that GitHub's
    // shared Windows runners measure production hardware latency.
    Assert(orekitTeme1000MedianMs > 0 && orekitTeme1000MedianMs < 15_000,
        $"Orekit TEME propagation performance regression: {orekitTeme1000MedianMs:F1} ms / 1000");
    Assert(orekitObserve1000MedianMs > 0 && orekitObserve1000MedianMs < 30_000,
        $"Orekit topocentric observation performance regression: {orekitObserve1000MedianMs:F1} ms / 1000");
    Assert(moonObserve200MedianMs > 0 && moonObserve200MedianMs < 30_000,
        $"Orekit lunar ephemeris observation performance regression: {moonObserve200MedianMs:F1} ms / 200");
    Console.WriteLine($"Orekit/IKVM warmed median: TEME 1000={orekitTeme1000MedianMs:F2} ms, " +
        $"station Observe 1000={orekitObserve1000MedianMs:F2} ms, " +
        $"Moon Observe 200={moonObserve200MedianMs:F2} ms; " +
        $"SGP.NET reference TEME 1000={sgpNetTeme1000MedianMs:F2} ms");

    var report = new
    {
        status = "P0_SMOKE_COMPLETED_NOT_ACCURACY_ACCEPTED",
        orekitVersion = "13.1.9",
        ikvmMavenSdkVersion = "1.12.2",
        dataFile = leapFile,
        dataSha256,
        eopSha256,
        orekitFiniteRangeRate,
        sgpNetFiniteRangeRate,
        sgpNetRangeRateResidualMps,
        eopQualityAt2026 = OrekitDataContext.DataQuality(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)),
        moonObservation,
        sunObservation,
        venusObservation,
        moonFiniteRangeRate,
        lunarDopplerHzAt432080,
        initialSetupMs = initializedMs,
        thousandPredictionsMs,
        orekitTeme1000MedianMs,
        sgpNetTeme1000MedianMs,
        orekitObserve1000MedianMs,
        moonObserve200MedianMs,
        processWorkingSetBytes = Environment.WorkingSet,
        comparisons,
        allVallado,
        sixDigitErrorMetres,
        analyticPasses,
        unboundedPass,
        orbitPasses,
        observation,
        baselineObservation,
        geodetic,
        baselineGeodetic,
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

static double MedianMilliseconds(Action action)
{
    var samples = new double[3];
    for (int i = 0; i < samples.Length; i++)
    {
        var sw = Stopwatch.StartNew();
        action();
        samples[i] = sw.Elapsed.TotalMilliseconds;
    }
    Array.Sort(samples);
    return samples[1];
}

static double Distance(double[] a, double[] b) =>
    Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
