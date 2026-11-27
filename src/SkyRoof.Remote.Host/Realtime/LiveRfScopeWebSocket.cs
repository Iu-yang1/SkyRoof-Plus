using System.Net.WebSockets;
using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public static class LiveRfScopeWebSocket
{
    public static void MapLiveRfScope(this WebApplication app) =>
        app.MapGet("/ws/rf-scope/{band}", StreamAsync).RequireAuthorization();

    private static async Task StreamAsync(HttpContext context, string band)
    {
        if (band is not ("main" or "sub") || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            return;
        }
        string name = Environment.GetEnvironmentVariable("SKYROOF_RF_SCOPE_BUS") ?? "skyroof-rf-scope";
        NamedSignalRing ring;
        try { ring = new NamedSignalRing(name + "-" + band, 64, 4096); }
        catch (InvalidDataException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        using (ring)
        using (var socket = await context.WebSockets.AcceptWebSocketAsync())
            await SendFramesAsync(context, socket, ring);
    }

    private static async Task SendFramesAsync(HttpContext context, WebSocket socket, NamedSignalRing ring)
    {
        var cursor = new SignalCursor();
        DateTimeOffset nextAuth = DateTimeOffset.MinValue;
        while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= nextAuth)
            {
                nextAuth = DateTimeOffset.UtcNow.AddSeconds(1);
                if (!await LiveAudioWebSocket.StillAuthorizedAsync(context))
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation,
                        "Access revoked", context.RequestAborted);
                    break;
                }
            }
            if (!ring.TryReadNext(cursor, out var signal) || signal is null)
            {
                await Task.Delay(20, context.RequestAborted);
                continue;
            }
            if (IcomLanRfScopeFrame.TryDecode(signal.Payload, out _))
                await socket.SendAsync(signal.Payload.AsMemory(), WebSocketMessageType.Binary,
                    true, context.RequestAborted);
        }
    }
}
