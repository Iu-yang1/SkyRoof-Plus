using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.Orekit;

namespace SkyRoof.Remote.Station;

internal sealed class IssOemRefreshService(IOrbitBackend backend,
    ILogger<IssOemRefreshService> logger) : BackgroundService
{
    internal const string DefaultUrl =
        "https://nasa-public-data.s3.amazonaws.com/iss-coords/current/ISS_OEM/ISS.OEM_J2K_EPH.txt";

    internal static string CachePath =>
        Environment.GetEnvironmentVariable("SKYROOF_ISS_OEM_PATH") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SkyRoof", "Remote", "orbits", "ISS.OEM_J2K_EPH.txt");

    internal static bool AutoEnabled
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("SKYROOF_ISS_OEM_AUTO");
            return bool.TryParse(configured, out bool enabled) ? enabled :
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SKYROOF_ISS_OEM_PATH"));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (backend is not OrekitOrbitBackend || !AutoEnabled) return;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        string path = CachePath;
        while (!stoppingToken.IsCancellationRequested)
        {
            bool success = false;
            try
            {
                var source = new Uri(Environment.GetEnvironmentVariable("SKYROOF_ISS_OEM_URL") ?? DefaultUrl);
                await IssOemCacheRefresh.RefreshAsync(client, source, path, DateTimeOffset.UtcNow, stoppingToken);
                logger.LogInformation("ISS OEM cache refreshed from {Source}", source.Host);
                success = true;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ISS OEM refresh failed; preserving prior ephemeris");
            }
            await Task.Delay(success ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
