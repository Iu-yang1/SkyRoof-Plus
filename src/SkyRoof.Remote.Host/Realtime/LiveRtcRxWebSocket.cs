using System.Net.WebSockets;
using System.Text;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public static class LiveRtcRxWebSocket
{
    public static void MapLiveRtcRx(this WebApplication app) =>
        app.MapGet("/ws/rtc-rx", StreamAsync).RequireAuthorization();

    private static async Task StreamAsync(HttpContext context)
    {
        if (!OperatingSystem.IsWindows() || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        var name = Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_BUS") ?? "skyroof-rx-audio";
        int channels = int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_CHANNELS"),
            out int configured) ? configured : 1;
        if (channels is < 1 or > 2) { context.Response.StatusCode = 400; return; }
        try
        {
            using var peer = new NativeRtcRxPeer(Environment.GetEnvironmentVariable("SKYROOF_RTC_ICE_SERVER") ?? "");
            using var ring = new NamedSignalRing(name, 64, 4096);
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await NegotiateAndStreamAsync(context, socket, ring, peer, channels);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
            BadImageFormatException or InvalidDataException or IOException or OperationCanceledException)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 503;
            else context.Abort();
        }
    }

    private static async Task NegotiateAndStreamAsync(HttpContext context, WebSocket socket,
        NamedSignalRing ring, NativeRtcRxPeer peer, int channels)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        using var offerTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        offerTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        string offer = await ReadOfferAsync(socket, offerTimeout.Token);
        string answer = await peer.AcceptAsync(offer, lifetime.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes(answer).AsMemory(), WebSocketMessageType.Text,
            true, lifetime.Token);
        var closed = socket.ReceiveAsync(new byte[1].AsMemory(), lifetime.Token).AsTask();
        var pump = PumpAsync(context, ring, peer, channels, lifetime.Token);
        try { await await Task.WhenAny(closed, pump); }
        finally
        {
            lifetime.Cancel();
            try { await pump; }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task<string> ReadOfferAsync(WebSocket socket, CancellationToken ct)
    {
        using var data = new MemoryStream();
        byte[] block = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(block.AsMemory(), ct);
            if (result.MessageType != WebSocketMessageType.Text || data.Length + result.Count > 65536)
                throw new InvalidDataException("Invalid RTC SDP offer.");
            data.Write(block, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(data.ToArray());
        }
    }

    private static async Task PumpAsync(HttpContext context, NamedSignalRing ring,
        NativeRtcRxPeer peer, int channels, CancellationToken ct)
    {
        var cursor = new SignalCursor();
        DateTimeOffset nextCheck = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= nextCheck)
            {
                nextCheck = DateTimeOffset.UtcNow.AddSeconds(1);
                if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return;
            }
            if (ring.TryReadNext(cursor, out var frame) && frame is not null)
                peer.Send(RxPcmPacket.Encode(frame, channels));
            else await Task.Delay(10, ct);
        }
    }
}
