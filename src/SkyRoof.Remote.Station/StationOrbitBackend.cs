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

        var directory = Environment.GetEnvironmentVariable("SKYROOF_OREKIT_DATA")
            ?? Path.Combine(AppContext.BaseDirectory, "orekit-data");
        OrekitDataContext.Initialize(directory);
        return new OrekitOrbitBackend();
    }
}
