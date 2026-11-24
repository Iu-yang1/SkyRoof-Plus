using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public static class CatalogEndpoints
{
    public static void MapStationCatalog(this WebApplication app) =>
        app.MapGet("/api/v1/catalog/satellites", SearchAsync).RequireAuthorization();

    private static async Task<IResult> SearchAsync(string? q, StationDeviceBridge station, CancellationToken ct)
    {
        try
        {
            var result = await station.SearchCatalogAsync(q ?? "", ct);
            return Results.Ok(result.Satellites.Select(s => new
            {
                s.SatelliteId, s.Norad, s.Name, s.Status, s.TleLine1, s.TleLine2,
                s.OrbitSource, s.OrbitState,
                transmitters = s.Transmitters.Select(t => new
                {
                    t.Id, t.Description, t.Alive, t.Status, t.Mode, t.UplinkMode,
                    link = t.Link
                })
            }));
        }
        catch (RpcException ex)
        { return Results.Problem(title: "Station satellite catalog", detail: ex.Status.Detail, statusCode: 503); }
    }
}
