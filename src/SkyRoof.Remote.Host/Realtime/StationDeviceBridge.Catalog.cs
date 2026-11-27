using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationDeviceBridge
{
    public async Task<CatalogSearchResult> SearchCatalogAsync(string query, CancellationToken ct) =>
        await new StationCatalog.StationCatalogClient(_channel.Value).SearchAsync(
            new CatalogSearchRequest { Query = query, Limit = 80 },
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
}
