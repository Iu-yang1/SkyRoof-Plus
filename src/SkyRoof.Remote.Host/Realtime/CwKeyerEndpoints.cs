using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public static class CwKeyerEndpoints
{
    public sealed record Speed(double Wpm);
    public sealed record Message(string Text);

    public static void MapCwKeyer(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/device/cw").RequireAuthorization();
        group.MapGet("/status", GetAsync);
        group.MapPut("/speed", SpeedAsync).RequireAuthorization(p => p.RequireRole("Operator", "Administrator"));
        group.MapPost("/send", SendAsync).RequireAuthorization(p => p.RequireRole("Operator", "Administrator"));
    }

    private static async Task<IResult> GetAsync(HttpContext context, StationDeviceBridge station)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        try { return Results.Ok(await station.GetCwStatusAsync(context.RequestAborted)); }
        catch (RpcException e) { return Failure(e); }
    }

    private static async Task<IResult> SpeedAsync(Speed speed, HttpContext context, StationDeviceBridge station)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        try { return Results.Ok(await station.SetCwSpeedAsync(speed.Wpm, context.RequestAborted)); }
        catch (RpcException e) { return Failure(e); }
    }

    private static async Task<IResult> SendAsync(Message message, HttpContext context, StationDeviceBridge station)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            var running = station.SendCwAsync(new CwSendRequest
            {
                Text = message.Text ?? "", SessionId = "cw:" + context.TraceIdentifier
            }, lifetime.Token);
            return Results.Ok(await TimedFt4Endpoints.AwaitAuthorizedAsync(context, running, lifetime));
        }
        catch (RpcException e) { return Failure(e); }
        finally { lifetime.Cancel(); }
    }

    private static IResult Failure(RpcException e) => Results.Problem(title: "CW keyer",
        detail: e.Status.Detail, statusCode: e.StatusCode switch
        {
            StatusCode.InvalidArgument => 400,
            StatusCode.FailedPrecondition => 409,
            _ => 503
        });
}
