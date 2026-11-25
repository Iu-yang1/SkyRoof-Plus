namespace SkyRoof.Remote.Host.Realtime;

public static partial class CameraEndpoints
{
    public sealed record RelativeMoveRequest(string Profile, double Pan, double Tilt, double Zoom);
    public sealed record PtzStopRequest(string Profile);

    private static void MapPtz(WebApplication app)
    {
        app.MapPost("/api/v1/camera/ptz/relative", RelativeMoveAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));
        app.MapPost("/api/v1/camera/ptz/stop", StopAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));
    }

    private static async Task<IResult> RelativeMoveAsync(RelativeMoveRequest request, HttpContext context)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        var settings = CameraSettings.Read();
        if (settings.Endpoint.Length == 0) return Unconfigured();
        try
        {
            using var client = settings.Open();
            await client.MoveRelativeAsync(request.Profile, request.Pan, request.Tilt, request.Zoom, context.RequestAborted);
            return Results.Ok(new { state = "accepted" });
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) when (CameraSettings.IsProtocolError(ex)) { return CameraFailure(ex); }
    }

    private static async Task<IResult> StopAsync(PtzStopRequest request, HttpContext context)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        var settings = CameraSettings.Read();
        if (settings.Endpoint.Length == 0) return Unconfigured();
        try
        {
            using var client = settings.Open();
            await client.StopPtzAsync(request.Profile, context.RequestAborted);
            return Results.Ok(new { state = "accepted" });
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) when (CameraSettings.IsProtocolError(ex)) { return CameraFailure(ex); }
    }
}
