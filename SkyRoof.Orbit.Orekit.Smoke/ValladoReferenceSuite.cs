using System.Globalization;
using System.Security.Cryptography;
using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.Orekit;

internal sealed record ValladoCaseMetrics(
    int CaseNumber, string ValidationClass,
    int Norad, string Regime, int ReferenceSamples, int ComparedSamples,
    double MaxOrekitPositionErrorMetres,
    double RmsOrekitPositionErrorMetres,
    double MaxOrekitVelocityErrorMetresPerSecond,
    double MaxSgpNetPositionErrorMetres,
    string? Error);

internal sealed record ValladoReferenceReport(
    int Cases, int ReferenceSamples, int ComparedSamples,
    int CasesWithErrors, double MaximumPositionErrorMetres,
    int NominalCases, int NominalComparedSamples,
    double MaximumNominalPositionErrorMetres,
    double MaximumNominalVelocityErrorMetresPerSecond,
    IReadOnlyList<ValladoCaseMetrics> Results);

internal static class ValladoReferenceSuite
{
    private readonly record struct TleCase(int Norad, string Line1, string Line2);
    private readonly record struct ReferenceSample(
        double Minutes, double[] PositionKm, double[] VelocityKmPerSecond);
    private sealed record ReferenceBlock(int Norad, List<ReferenceSample> Samples);

    internal static ValladoReferenceReport Run(
        string dataDirectory, IOrbitBackend orekit, IOrbitBackend sgpNet)
    {
        string root = Path.Combine(dataDirectory, "Vallado");
        string tlePath = Path.Combine(root, "SGP4-VER.TLE");
        string vectorPath = Path.Combine(root, "tcppver.out");
        CheckHash(tlePath,
            "d246d1d9d768ace445a38a965713fa9ba52d80fd8a41a0502ff83d7acffe2881");
        CheckHash(vectorPath,
            "4642deff9709d50ec56f805a3b0061952207a04253007a4be487c997a22b9d6b");

        var cases = ReadCases(tlePath);
        var vectors = ReadVectors(vectorPath);
        if (cases.Count != vectors.Count ||
            cases.Where((c, i) => c.Norad != vectors[i].Norad).Any())
            throw new InvalidDataException(
                "Vallado element sets do not match reference blocks in sequence.");

        int referenceCount = vectors.Sum(x => x.Samples.Count);
        if (cases.Count != 33 || referenceCount < 600)
            throw new InvalidDataException("Vallado dataset is incomplete.");

        // The last test reuses NORAD 20413 with a distinct TLE. Matching by
        // NORAD alone aliases its two epochs and creates multi-megametre
        // false errors; the upstream oracle is ordered by verification case.
        var results = cases.Select((c, i) =>
            Evaluate(i + 1, c, vectors[i].Samples, orekit, sgpNet)).ToArray();
        int errors = results.Count(x => x.Error is not null);
        int compared = results.Sum(x => x.ComparedSamples);
        double maximum = results.Max(x => x.MaxOrekitPositionErrorMetres);
        var unexpected = results.Where(x => x.Error is not null && x.Norad != 33334).ToArray();
        if (unexpected.Length > 0 || compared != referenceCount - 1 ||
            results.Count(x => x.Norad == 33334 && x.Error is not null) != 1)
            throw new InvalidDataException(
                $"Vallado valid-case coverage failure: {compared}/{referenceCount} " +
                $"samples, unexpected failures: {string.Join("; ", unexpected.Select(x => x.Error))}");

        // Verification cases 16 and 30-33 deliberately stress Lyddane,
        // error-code handling or extrapolation about 3.5 years from a TLE
        // epoch. Preserve every discrepancy in Results rather than treating
        // an unphysical stress orbit as routine short-horizon tracking.
        // These are source-identified cases in the SHA-256 pinned dataset.
        var nominal = results.Where(x => x.ValidationClass == "NOMINAL").ToArray();
        if (nominal.Length != 28 ||
            nominal.Any(x => x.Error is not null ||
                             x.ComparedSamples != x.ReferenceSamples ||
                             x.MaxOrekitPositionErrorMetres > 0.1 ||
                             x.MaxOrekitVelocityErrorMetresPerSecond > 0.001))
            throw new InvalidDataException(
                "Standard Vallado orbit verification failed 0.1 m / 0.001 m/s " +
                "TEME tolerances. See per-case metrics (stress cases remain reported).");

        int nominalSamples = nominal.Sum(x => x.ComparedSamples);
        double nominalPositionMax = nominal.Max(x => x.MaxOrekitPositionErrorMetres);
        double nominalVelocityMax = nominal.Max(x => x.MaxOrekitVelocityErrorMetresPerSecond);
        Console.WriteLine(
            $"Nominal Vallado SGP4/SDP4 accuracy accepted: {nominal.Length} cases, " +
            $"{nominalSamples} samples, Δr max={nominalPositionMax:F6} m, " +
            $"Δv max={nominalVelocityMax:F9} m/s; five stress cases remain separately diagnosed");
        Console.WriteLine(
            $"Vallado 33-case verification: reference={referenceCount}, compared={compared}, " +
            $"incomplete/failed={errors}, max TEME Δr={maximum:F6} m");
        foreach (var result in results)
            Console.WriteLine(
                $"  CASE {result.CaseNumber:D2} {result.ValidationClass} NORAD {result.Norad:D5} {result.Regime}: {result.ComparedSamples}/{result.ReferenceSamples} " +
                $"Orekit Δr max={result.MaxOrekitPositionErrorMetres:F3} m " +
                $"SGP.NET Δr max={result.MaxSgpNetPositionErrorMetres:F3} m " +
                $"{result.Error}");

        return new ValladoReferenceReport(
            cases.Count, referenceCount, compared, errors, maximum,
            nominal.Length, nominalSamples, nominalPositionMax, nominalVelocityMax, results);
    }

    private static ValladoCaseMetrics Evaluate(
        int caseNumber, TleCase item, IReadOnlyList<ReferenceSample>? reference,
        IOrbitBackend orekit, IOrbitBackend sgpNet)
    {
        string regime = MeanMotion(item.Line2) < 6.4 ? "SDP4" : "SGP4";
        string validationClass = caseNumber switch
        {
            16 => "LYDDANE_STRESS",
            30 => "EXPECTED_ERROR_CODE_4",
            31 => "EXPECTED_ERROR_CODE_2",
            32 => "EXPECTED_ERROR_CODE_3_STRESS",
            33 => "EXTREME_TLE_EXTRAPOLATION",
            _ => "NOMINAL"
        };
        if (reference is null || reference.Count == 0)
            return new(caseNumber, validationClass, item.Norad, regime, 0, 0, 0, 0, 0, 0,
                "Reference vectors missing");

        int compared = 0;
        double maxP = 0, sumSqP = 0, maxV = 0, maxSgpP = 0;
        string? failure = null;
        try
        {
            var track = new OrekitTleTrack(item.Line1, item.Line2);
            IOrbitTrack? sgpTrack;
            try { sgpTrack = sgpNet.CreateTleTrack(item.Line1, item.Line2); }
            catch { sgpTrack = null; }
            DateTimeOffset epoch = ParseTleEpoch(item.Line1);

            foreach (ReferenceSample sample in reference)
            {
                try
                {
                    OrbitState state = track.PredictTemeAtOffset(TimeSpan.FromMinutes(sample.Minutes));
                    double p = Delta(new[] { state.XMetres, state.YMetres, state.ZMetres }, sample.PositionKm);
                    double v = Delta(new[] { state.VxMetresPerSecond,
                        state.VyMetresPerSecond, state.VzMetresPerSecond }, sample.VelocityKmPerSecond);
                    if (!double.IsFinite(p) || !double.IsFinite(v))
                        throw new ArithmeticException(
                            "Non-finite reference or propagated orbit state");
                    maxP = Math.Max(maxP, p);
                    maxV = Math.Max(maxV, v);
                    sumSqP += p * p;
                    compared++;

                    if (sgpTrack is not null)
                    {
                        OrbitState baseline = sgpTrack.PredictTeme(epoch.AddMinutes(sample.Minutes));
                        double sgpDelta = Delta(
                            new[] { baseline.XMetres, baseline.YMetres, baseline.ZMetres },
                            sample.PositionKm);
                        if (double.IsFinite(sgpDelta))
                            maxSgpP = Math.Max(maxSgpP, sgpDelta);
                    }
                }
                catch (Exception ex)
                {
                    failure = $"t={sample.Minutes} min: {ex.GetType().Name}: {ex.Message}";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
        }

        return new ValladoCaseMetrics(
            caseNumber, validationClass, item.Norad, regime, reference.Count, compared,
            maxP, compared > 0 ? Math.Sqrt(sumSqP / compared) : 0,
            maxV, maxSgpP, failure);
    }

    private static List<TleCase> ReadCases(string path)
    {
        var result = new List<TleCase>();
        string? first = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("1 ", StringComparison.Ordinal))
                first = line.Length >= 69 ? line[..69] : line;
            if (!line.StartsWith("2 ", StringComparison.Ordinal) || first is null)
                continue;
            string second = line.Length >= 69 ? line[..69] : line;
            int id = int.Parse(first.AsSpan(2, 5), CultureInfo.InvariantCulture);
            result.Add(new TleCase(id, first, second));
            first = null;
        }
        return result;
    }

    private static List<ReferenceBlock> ReadVectors(string path)
    {
        var result = new List<ReferenceBlock>();
        ReferenceBlock? current = null;
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 2 && fields[1] == "xx" &&
                int.TryParse(fields[0], CultureInfo.InvariantCulture, out int id))
            {
                current = new ReferenceBlock(id, new List<ReferenceSample>());
                result.Add(current);
            }
            else if (current is not null && fields.Length >= 7 &&
                     double.TryParse(fields[0], NumberStyles.Float,
                         CultureInfo.InvariantCulture, out double minutes))
            {
                var position = fields.Skip(1).Take(3)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var velocity = fields.Skip(4).Take(3)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                current.Samples.Add(new ReferenceSample(minutes, position, velocity));
            }
        }
        return result;
    }

    private static double MeanMotion(string line2) =>
        double.Parse(line2.AsSpan(52, 11), CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTleEpoch(string line1)
    {
        int shortYear = int.Parse(line1.AsSpan(18, 2), CultureInfo.InvariantCulture);
        int year = shortYear >= 57 ? 1900 + shortYear : 2000 + shortYear;
        double dayOfYear = double.Parse(line1.AsSpan(20, 12), CultureInfo.InvariantCulture);
        return new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(dayOfYear - 1);
    }

    private static double Delta(double[] stateMetres, double[] referenceKm) =>
        Math.Sqrt(stateMetres.Zip(referenceKm,
            (actual, expected) => Math.Pow(actual - expected * 1000.0, 2)).Sum());

    private static void CheckHash(string path, string expected)
    {
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
        if (actual != expected)
            throw new InvalidDataException($"Vallado source hash mismatch: {path} ({actual})");
    }
}
