namespace SkyRoof.Remote.Station;

internal static class CatalogPaths
{
    internal static string Desktop => Path.GetFullPath(
        Environment.GetEnvironmentVariable("SKYROOF_SATELLITE_CATALOG") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyRoof", "Remote", "Satellites.json"));

    internal static string Satnogs => Desktop + ".satnogs.json";
    internal static string Celestrak => Desktop + ".celestrak.json";
}
