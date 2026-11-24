using System.Security.Claims;
using Grpc.Core;
using Microsoft.AspNetCore.Identity;
using SkyRoof.Remote.Host.Identity;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record Ft4TransmitRequest(string Message, long FrequencyHz,
    float AudioHz, float Gain, bool Odd);

public static class TimedFt4Endpoints
{
    public static void MapTimedFt4(this WebApplication app) =>
        app.MapPost("/api/v1/dsp/ft4/transmit", TransmitAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));

    private static async Task<IResult> TransmitAsync(Ft4TransmitRequest request, HttpContext context,
        DspWaveformBridge dsp, StationDeviceBridge station)
    {
        if (request.FrequencyHz <= 0 || string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "FT4 message and TX frequency required." });
        if (!await IsOperatorAsync(context)) return Results.Forbid();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            var waveform = await dsp.EncodeFt4Async(new Ft4EncodeRequest
            {
                Message = request.Message, AudioHz = request.AudioHz, Gain = request.Gain
            }, lifetime.Token);
            if (waveform.SampleRate != 48000)
                return Results.Problem(title: "Invalid DSP waveform sample rate");
            var tx = new TimedTxRequest
            {
                JobId = Guid.NewGuid().ToString("N"), SessionId = "ft4:" + context.TraceIdentifier,
                AuthorizedPrincipal = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
                FrequencyHz = request.FrequencyHz, Mode = "USB-D",
                TargetUtcNanoseconds = NextPacketUtc(request.Odd),
                Pcm16 = waveform.Pcm16, Sha256 = waveform.Sha256
            };
            var running = station.RunTimedTxAsync(tx, lifetime.Token);
            var result = await AwaitAuthorizedAsync(context, running, lifetime);
            return Results.Ok(new { jobId = result.JobId, state = result.State,
                targetUtc = DateTime.UnixEpoch.AddTicks(tx.TargetUtcNanoseconds / 100),
                actualFirstPacketUtc = DateTime.UnixEpoch.AddTicks(result.ActualFirstPacketUtcNanoseconds / 100),
                sourceSamples = result.SourceSamples });
        }
        catch (RpcException error)
        {
            return Results.Problem(title: "FT4 transmit failed", detail: error.Status.Detail,
                statusCode: error.StatusCode is StatusCode.InvalidArgument ? 400 : 409);
        }
        finally { lifetime.Cancel(); }
    }

    private static long NextPacketUtc(bool odd)
    {
        const long slot = 7_500_000_000;
        long now = (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;
        // Encode and seal before keying; choose next parity with >=2 s lead.
        long index = (now + 2_000_000_000 + slot - 1) / slot;
        if ((index % 2 == 1) != odd) index++;
        long delay = long.TryParse(Environment.GetEnvironmentVariable("SKYROOF_FT4_TX_DELAY_MS"),
            out long ms) ? ms : 0;
        return checked(index * slot + 150_000_000 - delay * 1_000_000);
    }

    private static async Task<bool> IsOperatorAsync(HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return false;
        var users = context.RequestServices.GetRequiredService<UserManager<StationUser>>();
        var user = await users.GetUserAsync(context.User);
        if (user is null) return false;
        var roles = await users.GetRolesAsync(user);
        return roles.Contains("Operator") || roles.Contains("Administrator");
    }

    private static async Task<TimedTxReceipt> AwaitAuthorizedAsync(HttpContext context,
        Task<TimedTxReceipt> running, CancellationTokenSource lifetime)
    {
        while (!running.IsCompleted)
        {
            var delay = Task.Delay(1000, lifetime.Token);
            if (await Task.WhenAny(running, delay) == running) break;
            await delay;
            if (!await IsOperatorAsync(context))
            {
                lifetime.Cancel();
                break;
            }
        }
        return await running;
    }
}
