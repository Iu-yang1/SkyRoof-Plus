using System.Globalization;
using System.Security.Cryptography;
using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.Orekit;

internal sealed record ValladoCaseMetrics(
    int Norad, string Regime, int ReferenceSamples, int ComparedSamples,
    double MaxOrekitPositionErrorMetres,
    double RmsOrekitPositionErrorMetres,
    double MaxOrekitVelocityErrorMetresPerSecond,
    double MaxSgpNetPositionErrorMetres,
    string? Error);

internal sealed record ValladoReferenceReport(
    int Cases, int ReferenceSamples, int ComparedSamples,
    int CasesWithErrors, double MaximumPositionErrorMetres,
    IReadOnlyList<ValladoCaseMetrics> Results);

internal static class ValladoReferenceSuite
{
    private readonly record struct TleCase(int Norad, string Line1, string Line2);
    private readonly record struct ReferenceSample(
        double Minutes, double[] PositionKm, double[] VelocityKmPerSecond);

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
        int referenceCount = vectors.Values.Sum(x => x.Count);
        if (cases.Count < 30 || referenceCount < 600)
            throw new InvalidDataException("Vallado dataset is incomplete.");

        var results = cases.Select(c =>
            Evaluate(c, vectors.GetValueOrDefault(c.Norad), orekit, sgpNet)).ToArray();
        int errors = results.Count(x => x.Error is not null);
        int compared = results.Sum(x => x.ComparedSamples);
        double maximum = results.Max(x => x.MaxOrekitPositionErrorMetres);
        Console.WriteLine(
            $"Vallado 33-case verification: reference={referenceCount}, compared={compared}, " +
            $"incomplete/failed={errors}, max TEME Δr={maximum:F6} m");
        foreach (var result in results)
            Console.WriteLine(
                $"  NORAD {result.Norad:D5} {result.Regime}: {result.ComparedSamples}/{result.ReferenceSamples} " +
                $"Orekit Δr max={result.MaxOrekitPositionErrorMetres:F3} m " +
                $"SGP.NET Δr max={result.MaxSgpNetPositionErrorMetres:F3} m " +
                $"{result.Error}");

        return new ValladoReferenceReport(
            cases.Count, referenceCount, compared, errors, maximum, results);
    }

    private static ValladoCaseMetrics Evaluate(
        TleCase item, IReadOnlyList<ReferenceSample>? reference,
        IOrbitBackend orekit, IOrbitBackend sgpNet)
    {
        string regime = MeanMotion(item.Line2) < 6.4 ? "SDP4" : "SGP4";
        if (reference is null || reference.Count == 0)
            return new(item.Norad, regime, 0, 0, 0, 0, 0, 0, "Reference vectors missing");

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
                    maxP = Math.Max(maxP, p);
                    maxV = Math.Max(maxV, v);
                    sumSqP += p * p;
                    compared++;

                    if (sgpTrack is not null)
                    {
                        OrbitState baseline = sgpTrack.PredictTeme(epoch.AddMinutes(sample.Minutes));
                        maxSgpP = Math.Max(maxSgpP,
                            Delta(new[] { baseline.XMetres, baseline.YMetres, baseline.ZMetres },
                                  sample.PositionKm));
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
            item.Norad, regime, reference.Count, compared,
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

    private static Dictionary<int, List<ReferenceSample>> ReadVectors(string path)
    {
        var result = new Dictionary<int, List<ReferenceSample>>();
        int? active = null;
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 2 && fields[1] == "xx" &&
                int.TryParse(fields[0], CultureInfo.InvariantCulture, out int id))
            {
                active = id;
                result.TryAdd(id, new List<ReferenceSample>());
            }
            else if (active is not null && fields.Length >= 7 &&
                     double.TryParse(fields[0], NumberStyles.Float,
                         CultureInfo.InvariantCulture, out double minutes))
            {
                var p = fields.Skip(1).Take(3)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var v = fields.Skip(4).Take(3)
                    .Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                result[active.Value].Add(new ReferenceSample(minutes, p, v));
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
