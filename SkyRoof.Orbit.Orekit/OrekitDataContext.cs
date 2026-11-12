using org.orekit.data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyRoof.Orbit.Orekit;

/// <summary>
/// Initializes the process-wide Orekit data provider once, before requesting UTC/frames.
/// Supplying a different directory requires a new process (Orekit caches time scales).
/// </summary>
public static class OrekitDataContext
{
    private static readonly object Sync = new();
    private static string? initializedDirectory;
    private static DateTimeOffset? eopFirst, eopLast;

    public static string Initialize(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string absoluteDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(absoluteDirectory))
            throw new DirectoryNotFoundException($"Orekit data directory not found: {absoluteDirectory}");

        if (!File.Exists(Path.Combine(absoluteDirectory, "tai-utc.dat")) &&
            !File.Exists(Path.Combine(absoluteDirectory, "UTC-TAI.history")))
            throw new FileNotFoundException(
                "Orekit UTC-TAI history is missing (tai-utc.dat or UTC-TAI.history).",
                absoluteDirectory);

        lock (Sync)
        {
            if (initializedDirectory == absoluteDirectory)
                return initializedDirectory;

            if (initializedDirectory is not null)
                throw new InvalidOperationException(
                    $"Orekit is initialized with '{initializedDirectory}'; a different data set requires a new process.");

            ReadEopCoverage(absoluteDirectory);
            DataContext.getDefault().getDataProvidersManager()
                .addProvider(new DirectoryCrawler(new java.io.File(absoluteDirectory)));
            initializedDirectory = absoluteDirectory;
            return initializedDirectory;
        }
    }

    public static string DataQuality(DateTimeOffset epoch)
    {
        if (eopFirst is null || eopLast is null)
            return "UTC_ONLY_EOP_MISSING";
        if (epoch < eopFirst || epoch > eopLast)
            return "EOP_OUTSIDE_DATA_COVERAGE";
        return "IERS_FINALS2000A_RAPID_OR_PREDICTED";
    }

    private static void ReadEopCoverage(string directory)
    {
        string path = Path.Combine(directory, "finals2000A.all");
        if (!File.Exists(path)) return;
        var mjd = new Regex(@"^\s*\d{2}\s+\d{1,2}\s+\d{1,2}\s+(\d{5}\.\d+)", RegexOptions.CultureInvariant);
        double? low = null, high = null;
        foreach (string line in File.ReadLines(path))
        {
            Match match = mjd.Match(line);
            if (!match.Success || !double.TryParse(match.Groups[1].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double day))
                continue;
            low = Math.Min(low ?? day, day);
            high = Math.Max(high ?? day, day);
        }
        if (low is not null && high is not null)
        {
            var zero = new DateTimeOffset(1858, 11, 17, 0, 0, 0, TimeSpan.Zero);
            eopFirst = zero.AddDays(low.Value);
            eopLast = zero.AddDays(high.Value);
        }
    }
}
