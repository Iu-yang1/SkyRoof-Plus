using VE3NEA;
using System.Reflection;
using Serilog;
using SkyRoof.Orbit.Contracts;

namespace SkyRoof;

/// <summary>
/// Application-level backend selection. Orekit's Java assemblies are loaded
/// only when explicitly selected; legacy SGP.NET remains built into SkyRoof.
/// </summary>
internal static class OrbitBackendRegistry
{
    private static readonly object Sync = new();
    private static IOrbitBackend? selectedBackend;
    private static string dataQuality = "SGPNET_BASELINE";

    internal static OrbitEngineMode Mode { get; private set; } = OrbitEngineMode.SgpNet;

    internal readonly record struct Snapshot(OrbitEngineMode Mode, IOrbitBackend? Backend, string Quality);

    internal static Snapshot Capture()
    {
        lock (Sync)
            return new Snapshot(Mode, selectedBackend, dataQuality);
    }

    internal static void Restore(Snapshot snapshot)
    {
        lock (Sync)
        {
            selectedBackend = snapshot.Backend;
            Mode = snapshot.Mode;
            dataQuality = snapshot.Quality;
        }
    }

    internal static string CurrentBackendId
    {
        get
        {
            lock (Sync)
                return selectedBackend?.BackendId ?? "sgpnet";
        }
    }

    internal static string CurrentDataQuality
    {
        get
        {
            lock (Sync)
                return dataQuality;
        }
    }

    internal static void Configure(OrbitSourceSettings settings)
    {
        if (settings.Backend == OrbitEngineMode.SgpNet)
        {
            lock (Sync)
            {
                selectedBackend = null;
                Mode = OrbitEngineMode.SgpNet;
                dataQuality = "SGPNET_BASELINE";
            }
            return;
        }

        string dataDir = ResolveDataDirectory(settings.OrekitDataDirectory);
        EnsureLeapSecondHistory(dataDir);

        string path = Path.Combine(AppContext.BaseDirectory, "SkyRoof.Orbit.Orekit.dll");
        if (!File.Exists(path))
            throw new FileNotFoundException("Orekit backend assembly is not installed.", path);

        Assembly assembly = Assembly.LoadFrom(path);
        Type dataType = assembly.GetType("SkyRoof.Orbit.Orekit.OrekitDataContext", throwOnError: true)
            ?? throw new TypeLoadException("OrekitDataContext was not found in the selected backend.");
        MethodInfo initialize = dataType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(dataType.FullName, "Initialize");
        initialize.Invoke(null, [dataDir]);
        MethodInfo qualityMethod = dataType.GetMethod("DataQuality", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(dataType.FullName, "DataQuality");
        string quality = qualityMethod.Invoke(null, [DateTimeOffset.UtcNow]) as string
            ?? throw new InvalidOperationException("Orekit data quality status unavailable");
        Type backendType = assembly.GetType("SkyRoof.Orbit.Orekit.OrekitOrbitBackend", throwOnError: true)
            ?? throw new TypeLoadException("OrekitOrbitBackend was not found in the selected backend.");
        if (Activator.CreateInstance(backendType) is not IOrbitBackend backend)
            throw new InvalidOperationException("Orekit backend does not implement the current orbit contracts.");

        lock (Sync)
        {
            selectedBackend = backend;
            Mode = OrbitEngineMode.Orekit;
            dataQuality = quality;
        }
        Log.Information("Orbit backend selected: {Backend} (data: {Directory})",
            backend.BackendId, dataDir);
    }

    internal static IOrbitTrack? CreateTrack(SatnogsDbTle elements)
    {
        IOrbitBackend? backend;
        lock (Sync)
            backend = selectedBackend;
        if (backend is null)
            return null;

        if (elements.omm is { } omm)
        {
            DateTime utcEpoch = DateTime.SpecifyKind(omm.Epoch, DateTimeKind.Utc);
            var model = new OrbitMeanElements(
                checked((int)omm.NoradCatID), omm.ObjectID ?? "",
                string.IsNullOrWhiteSpace(omm.ClassificationType) ? 'U' : omm.ClassificationType[0],
                new DateTimeOffset(utcEpoch),
                omm.MeanMotion, omm.MeanMotionDot, omm.MeanMotionDDot,
                omm.Eccentricity, omm.Inclination, omm.RAOfAscNode,
                omm.ArgOfPericenter, omm.MeanAnomaly, omm.BStar,
                omm.EphemerisType, checked((int)omm.ElementSetNo),
                checked((int)omm.RevAtEpoch));
            return backend.CreateOmmTrack(model);
        }

        return backend.CreateTleTrack(elements.tle1, elements.tle2);
    }

    internal static string ResolveDataDirectory(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        return Path.Combine(Utils.GetUserDataFolder(), "orekit-data");
    }

    private static void EnsureLeapSecondHistory(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        string target = Path.Combine(dataDir, "tai-utc.dat");
        if (File.Exists(target) || File.Exists(Path.Combine(dataDir, "UTC-TAI.history")))
            return;

        string source = Path.Combine(AppContext.BaseDirectory, "orekit-data", "tai-utc.dat");
        if (!File.Exists(source))
            throw new FileNotFoundException("Orekit UTC-TAI data is missing. Install or import tai-utc.dat.", source);
        File.Copy(source, target);
    }
}
