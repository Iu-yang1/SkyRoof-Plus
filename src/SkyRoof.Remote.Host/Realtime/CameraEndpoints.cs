using SkyRoof;

namespace SkyRoof.Remote.Host.Realtime;

public static class CameraEndpoints
{
    public static void MapCamera(this WebApplication app)
    {
        app.MapGet("/api/v1/camera/profiles", (Delegate)ProfilesAsync).RequireAuthorization();
        app.MapGet("/api/v1/camera/snapshot", SnapshotAsync).RequireAuthorization();
        app.MapPost("/api/v1/camera/discover", (Delegate)DiscoverAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));
    }

    private static async Task<IResult> DiscoverAsync(HttpContext context)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        var found = await OnvifDiscovery.ProbeWithDiagnosticsAsync(context.RequestAborted);
        return Results.Ok(found);
    }

    private static async Task<IResult> ProfilesAsync(HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        var settings = CameraSettings.Read();
        if (settings.Endpoint.Length == 0) return Unconfigured();
        try
        {
            using var client = settings.Open();
            return Results.Ok(await client.GetProfilesAsync(context.RequestAborted));
        }
        catch (Exception error) when (CameraSettings.IsProtocolError(error))
        { return CameraFailure(error); }
    }

    private static async Task<IResult> SnapshotAsync(string profile, bool media2, HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        var settings = CameraSettings.Read();
        if (settings.Endpoint.Length == 0) return Unconfigured();
        try
        {
            using var client = settings.Open();
            var uri = await client.GetSnapshotUriAsync(new OnvifMediaProfile(profile, "", media2), context.RequestAborted);
            var snapshot = await settings.ReadSnapshotAsync(uri, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(snapshot.Bytes, snapshot.ContentType);
        }
        catch (Exception error) when (CameraSettings.IsProtocolError(error))
        { return CameraFailure(error); }
    }

    private static IResult Unconfigured() => Results.Problem(title: "No station camera configured", statusCode: 503);
    private static IResult CameraFailure(Exception error) =>
        Results.Problem(title: "Station camera unavailable", detail: error.GetType().Name, statusCode: 502);
}
