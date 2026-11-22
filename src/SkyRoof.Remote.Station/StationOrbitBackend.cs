using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.Orekit;
using SkyRoof.Orbit.SgpNet;

namespace SkyRoof.Remote.Station;

internal static class StationOrbitBackend
{
    internal static IOrbitBackend Create()
    {
        var selected = Environment.GetEnvironmentVariable("SKYROOF_ORBIT_BACKEND") ?? "orekit";
        if (selected.Equals("sgpnet", StringComparison.OrdinalIgnoreCase))
            return new SgpNetOrbitBackend();
        if (!selected.Equals("orekit", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unknown Station orbit backend: " + selected);

        var directory = DataDirectory();
        OrekitDataContext.Initialize(directory);
        return new OrekitOrbitBackend();
    }

    internal static string DataDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("SKYROOF_OREKIT_DATA");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        // Keep mutable EOP files outside read-only program installation directories.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "SkyRoof", "Remote", "orekit-data");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "tai-utc.dat");
        var source = Path.Combine(AppContext.BaseDirectory, "orekit-data", "tai-utc.dat");
        if (!File.Exists(target) && File.Exists(source)) File.Copy(source, target);
        return directory;
    }
}
