using org.orekit.data;

namespace SkyRoof.Orbit.Orekit;

/// <summary>
/// Initializes the process-wide Orekit data provider once, before requesting UTC/frames.
/// Supplying a different directory requires a new process (Orekit caches time scales).
/// </summary>
public static class OrekitDataContext
{
    private static readonly object Sync = new();
    private static string? initializedDirectory;

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

            DataContext.getDefault().getDataProvidersManager()
                .addProvider(new DirectoryCrawler(new java.io.File(absoluteDirectory)));
            initializedDirectory = absoluteDirectory;
            return initializedDirectory;
        }
    }
}
