using System.Net.WebSockets;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SkyRoof.Remote.Host.Identity;
using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public static class LiveAudioWebSocket
{
    public static void MapLiveAudioStream(this WebApplication app)
    {
        app.MapGet("/ws/audio", StreamAsync).RequireAuthorization();
    }

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
        var name = Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_BUS")
            ?? "skyroof-rx-audio";
        var channelRaw = Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_CHANNELS");
        var channels = int.TryParse(channelRaw, out var setting) ? setting : 1;
        if (channels is < 1 or > 2)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        NamedSignalRing ring;
        try
        {
            ring = new NamedSignalRing(name, slots: 64, payloadCapacity: 4096);
        }
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
            while (socket.State == WebSocketState.Open &&
                !context.RequestAborted.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextAuthorizationCheck)
                {
                    nextAuthorizationCheck = DateTimeOffset.UtcNow.AddSeconds(1);
                    if (!await StillAuthorizedAsync(context))
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                            "Access revoked", context.RequestAborted);
                        break;
                    }
                }
                if (!ring.TryReadNext(cursor, out var frame) || frame is null)
                {
                    await Task.Delay(10, context.RequestAborted);
                    continue;
                }
                if (frame.SampleCount is < 1 or > ushort.MaxValue ||
                    frame.Payload.Length != checked(frame.SampleCount * channels * 2))
                    throw new InvalidDataException("SignalBus PCM16 geometry is inconsistent.");

                var header = new AudioFrameHeader
                {
                    Codec = 2,
                    Flags = 0,
                    StreamId = 1,
                    StreamEpoch = checked((uint)frame.StreamEpoch),
                    Sequence = checked((ulong)frame.Sequence),
                    FirstSampleIndex = frame.FirstSampleIndex,
                    UtcNanoseconds = 0,
                    SampleRate = checked((uint)frame.SampleRate),
                    SamplesPerChannel = checked((ushort)frame.SampleCount),
                    Channels = (byte)channels,
                    PayloadLength = checked((uint)frame.Payload.Length)
                };
                var outgoing = new byte[AudioFrameHeader.Size + frame.Payload.Length];
                header.Write(outgoing);
                frame.Payload.AsSpan().CopyTo(outgoing.AsSpan(AudioFrameHeader.Size));
                await socket.SendAsync(outgoing.AsMemory(), WebSocketMessageType.Binary,
                    endOfMessage: true, context.RequestAborted);
            }
        }
    }
    private static async Task<bool> StillAuthorizedAsync(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var db = context.RequestServices.GetRequiredService<StationIdentityDb>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u =>
            u.Id == userId, context.RequestAborted);
        if (user is null || user.LockoutEnd > DateTimeOffset.UtcNow) return false;
        var credential = context.User.FindFirstValue("skyroof_credential");
        if (credential is null) return true;
        if (!Guid.TryParse(credential, out var id)) return false;
        var token = await db.AccessTokens.AsNoTracking().SingleOrDefaultAsync(t =>
            t.Id == id && t.OwnerUserId == userId && t.RevokedUtc == null,
            context.RequestAborted);
        return token is not null && token.ExpiresUtc > DateTimeOffset.UtcNow;
    }

}
