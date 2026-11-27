using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class StationCatalogService(StationCatalogCache cache) : StationCatalog.StationCatalogBase
{
    public override Task<CatalogSearchResult> Search(CatalogSearchRequest request, ServerCallContext context)
    {
        try
        {
            var entries = cache.Search(request.Query, request.Limit);
            var result = new CatalogSearchResult();
            result.Satellites.AddRange(entries);
            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            throw new RpcException(new Status(StatusCode.Unavailable,
                "Station satellite catalog unavailable. Check the configured SkyRoof Satellites.json cache."));
        }
    }
}
