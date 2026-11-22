using System.Buffers.Binary;
using System.Net.WebSockets;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

/// <summary>
/// Audio-frequency FFT, not a receiver RF/IF scope. Only verified 475-bin
/// snapshots from the independent DSP service reach this authenticated route.
/// </summary>
public static class LiveAfSpectrumWebSocket
{
    public static void MapLiveAfSpectrum(this WebApplication app) =>
        app.MapGet("/ws/af-spectrum", StreamAsync).RequireAuthorization();

    private static async Task StreamAsync(HttpContext context)
    {
        if (!OperatingSystem.IsWindows())
        {
            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var name = Environment.GetEnvironmentVariable("SKYROOF_AF_SPECTRUM_BUS")
            ?? "skyroof-af-spectrum";
        NamedSignalRing ring;
        try { ring = new NamedSignalRing(name, slots: 64, payloadCapacity: 4096); }
        catch (InvalidDataException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        using (ring)
        using (var socket = await context.WebSockets.AcceptWebSocketAsync())
        {
            var cursor = new SignalCursor();
            var nextAuthorizationCheck = DateTimeOffset.MinValue;
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextAuthorizationCheck)
                {
                    nextAuthorizationCheck = DateTimeOffset.UtcNow.AddSeconds(1);
                    if (!await LiveAudioWebSocket.StillAuthorizedAsync(context))
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation,
                            "Access revoked", context.RequestAborted);
                        break;
                    }
                }
                if (!ring.TryReadNext(cursor, out var signal) || signal is null)
                {
                    await Task.Delay(15, context.RequestAborted);
                    continue;
                }
                if (signal.SampleRate != 48000 || signal.SampleCount != 475 ||
                    signal.Payload.Length != 475 * 4)
                    throw new InvalidDataException("Invalid DSP AF spectrum geometry.");
                var frame = new byte[24 + signal.Payload.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), 0x31505353);
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 1);
                BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), signal.Sequence);
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16, 4), 475);
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20, 4), 48000);
                signal.Payload.AsSpan().CopyTo(frame.AsSpan(24));
                await socket.SendAsync(frame.AsMemory(), WebSocketMessageType.Binary, true,
                    context.RequestAborted);
            }
        }
    }
}
