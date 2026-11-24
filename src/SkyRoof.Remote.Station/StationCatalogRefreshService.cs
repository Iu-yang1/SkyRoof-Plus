namespace SkyRoof.Remote.Station;

internal sealed class StationCatalogRefreshService(ILogger<StationCatalogRefreshService> logger) : BackgroundService
{
    private const string SatnogsApi = "https://db.satnogs.org/api/";
    private const string CelestrakApi = "https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=CSV";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("SKYROOF_CATALOG_AUTO"), "false",
            StringComparison.OrdinalIgnoreCase)) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SkyRoof-Remote/1.7");
        await Task.WhenAll(
            RunSourceAsync("SatNOGS", CatalogPaths.Satnogs, TimeSpan.FromHours(24),
                ct => CatalogOnlineFetcher.FetchSatnogsAsync(http, new Uri(
                    Environment.GetEnvironmentVariable("SKYROOF_SATNOGS_API") ?? SatnogsApi), ct), stoppingToken),
            RunSourceAsync("CelesTrak", CatalogPaths.Celestrak, TimeSpan.FromHours(6),
                ct => CatalogOnlineFetcher.FetchCelestrakAsync(http, new Uri(
                    Environment.GetEnvironmentVariable("SKYROOF_CELESTRAK_OMM_URL") ?? CelestrakApi), ct), stoppingToken));
    }

    private async Task RunSourceAsync(string name, string path, TimeSpan interval,
        Func<CancellationToken, Task<CatalogCacheSatellite[]>> fetch, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay = File.Exists(path) ? interval - (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)) : TimeSpan.Zero;
            if (delay > TimeSpan.Zero) await Task.Delay(delay > interval ? interval : delay, ct);
            try
            {
                var data = await fetch(ct);
                await CatalogOnlineFetcher.SaveAsync(path, data, ct);
                logger.LogInformation("Updated {Source} catalog, {Count} records", name, data.Length);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{Source} catalog refresh failed; retaining last good snapshot", name);
                await Task.Delay(interval, ct);
            }
        }
    }
}
