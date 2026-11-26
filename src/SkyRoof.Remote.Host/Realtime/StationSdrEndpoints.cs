using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public static class StationSdrEndpoints
{
    public static void MapStationSdr(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/sdr").RequireAuthorization();
        group.MapGet("/", ReadAsync);
        group.MapPut("/", StartAsync).RequireAuthorization(p => p.RequireRole("Operator", "Administrator"));
        group.MapDelete("/", StopAsync).RequireAuthorization(p => p.RequireRole("Operator", "Administrator"));
    }
    private static async Task<IResult> ReadAsync(HttpContext context, StationDeviceBridge station)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        return await ExecuteAsync(() => station.ReadSdrAsync(context.RequestAborted));
    }
    private static async Task<IResult> StartAsync(SdrCaptureRequest request, HttpContext context, StationDeviceBridge station)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        return await ExecuteAsync(() => station.StartSdrAsync(request, context.RequestAborted));
    }
    private static async Task<IResult> StopAsync(HttpContext context, StationDeviceBridge station)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        return await ExecuteAsync(() => station.StopSdrAsync(context.RequestAborted));
    }
    private static async Task<IResult> ExecuteAsync(Func<Task<SdrStatus>> action)
    {
        try { return Results.Ok(await action()); }
        catch (RpcException e)
        { return Results.Problem(title: "SDR control failed", detail: e.Status.Detail,
            statusCode: e.StatusCode == StatusCode.InvalidArgument ? 400 : 503); }
    }
}
