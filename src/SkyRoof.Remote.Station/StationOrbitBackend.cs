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

        // Keep mutable EOP files outside read-only program installation directories.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(root, "SkyRoof", "Remote", "orekit-data") : Path.GetFullPath(configured);
        Directory.CreateDirectory(directory);
        foreach (string file in new[] { "tai-utc.dat", "lnxp1990.430" })
        {
            var target = Path.Combine(directory, file);
            var source = Path.Combine(AppContext.BaseDirectory, "orekit-data", file);
            if (!File.Exists(target) && File.Exists(source)) File.Copy(source, target);
        }
        return directory;
    }
}
