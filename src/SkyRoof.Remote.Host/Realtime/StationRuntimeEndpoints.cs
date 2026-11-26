using Grpc.Core;

namespace SkyRoof.Remote.Host.Realtime;

internal static class StationRuntimeEndpoints
{
    internal const string ExpectedInstanceHeader = "X-SkyRoof-Station-Instance";

    internal static void MapRuntimeIdentity(this WebApplication app) =>
        app.MapGet("/api/v1/station/runtime", IdentifyAsync).RequireAuthorization();

    private static async Task<IResult> IdentifyAsync(HttpContext context, StationDeviceBridge station)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        try
        {
            var identity = await station.IdentifyAsync(context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { identity.StationId, identity.InstanceId });
        }
        catch (RpcException error)
        { return Results.Problem(title: "Station unavailable", detail: error.Status.Detail, statusCode: 503); }
    }

    // The desktop may copy RF settings or a Moon job from its local pipe.
    // Bind that request to the same live Station before any DSP or TX work.
    // Existing Web clients already operate wholly through one Host.
    internal static async Task<bool> MatchExpectedInstanceAsync(HttpContext context, StationDeviceBridge station)
    {
        if (!context.Request.Headers.TryGetValue(ExpectedInstanceHeader, out var expected)) return true;
        try
        {
            var identity = await station.IdentifyAsync(context.RequestAborted);
            if (expected.Count == 1 && string.Equals(expected[0], identity.InstanceId, StringComparison.Ordinal))
                return true;
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { error = "Selected local Station differs from Host or restarted." });
        }
        catch (RpcException)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { error = "Station identity unavailable." });
        }
        return false;
    }
}
