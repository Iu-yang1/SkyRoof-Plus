using System.Buffers.Binary;
using System.Net.WebSockets;
using SkyRoof.Remote.Foundation.Signals;

namespace SkyRoof.Remote.Host.Realtime;

// Simulation only. These routes do not connect to radio PTT, RF, or signal hardware.
public static class SimulatedBinaryStreams
{
    public static void MapSimulatedBinaryStreams(this WebApplication app)
    {
        app.MapGet("/sim/ws/audio", EchoAudioAsync).RequireAuthorization();
        app.MapGet("/sim/ws/spectrum", StreamSpectrumAsync).RequireAuthorization();
    }

    private static async Task EchoAudioAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var bytes = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            var received = await socket.ReceiveAsync(bytes, context.RequestAborted);
            if (received.MessageType == WebSocketMessageType.Close) break;
            if (!received.EndOfMessage || received.MessageType != WebSocketMessageType.Binary
                || !AudioFrameHeader.TryRead(bytes.AsSpan(0, received.Count), out var header)
                || header.PayloadLength != received.Count - AudioFrameHeader.Size)
            {
                await socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData,
                    "Invalid SAP1 frame", context.RequestAborted);
                break;
            }
            await socket.SendAsync(bytes.AsMemory(0, received.Count),
                WebSocketMessageType.Binary, true, context.RequestAborted);
        }
    }

    private static async Task StreamSpectrumAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var interval = new PeriodicTimer(TimeSpan.FromMilliseconds(33.333));
        var frame = new byte[24 + 475 * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), 0x31505353);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16, 4), 475);
        var sequence = 0L;
        while (socket.State == WebSocketState.Open &&
            await interval.WaitForNextTickAsync(context.RequestAborted))
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), sequence++);
            for (var bin = 0; bin < 475; bin++)
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24 + bin * 4, 4),
                    BitConverter.SingleToInt32Bits(-120f + 20f * MathF.Sin(bin * .04f)));
            await socket.SendAsync(frame.AsMemory(), WebSocketMessageType.Binary,
                true, context.RequestAborted);
        }
    }
}
