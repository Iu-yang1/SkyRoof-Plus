using System.Security.Claims;
using SkyRoof.Remote.Foundation.Digital;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public static partial class Q65Endpoints
{
    public sealed record TransmitRequest(string Submode, string Message, long FrequencyHz, float AudioHz, float Gain, bool Odd);

    private static async Task<IResult> TransmitAsync(TransmitRequest request, HttpContext context,
        DspWaveformBridge dsp, StationDeviceBridge station)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        if (request.FrequencyHz <= 0 || string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "Q65 requires a message and TX frequency." });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            var encoded = await dsp.EncodeQ65Async(new Q65EncodeRequest
            {
                Submode = request.Submode, Message = request.Message, AudioHz = request.AudioHz, Gain = request.Gain
            }, lifetime.Token);
            var pcm = encoded.Waveform;
            if (pcm is null || pcm.SampleRate != 48000 || encoded.SlotSeconds is not (15 or 30 or 60 or 120 or 300))
                return Results.Problem(title: "Invalid Q65 DSP waveform.");
            long target = TargetUtc(encoded.SlotSeconds, encoded.TxStartSamples12K, request.Odd);
            var job = new TimedTxRequest
            {
                JobId = Guid.NewGuid().ToString("N"), SessionId = "q65:" + context.TraceIdentifier,
                AuthorizedPrincipal = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
                FrequencyHz = request.FrequencyHz, Mode = "USB-D", TargetUtcNanoseconds = target,
                Pcm16 = pcm.Pcm16, Sha256 = pcm.Sha256
            };
            var receipt = await TimedFt4Endpoints.AwaitAuthorizedAsync(context,
                station.RunTimedTxAsync(job, lifetime.Token), lifetime);
            return Results.Ok(new { receipt.State, receipt.JobId, receipt.SourceSamples,
                targetUtc = DateTime.UnixEpoch.AddTicks(target / 100),
                actualFirstPacketUtc = DateTime.UnixEpoch.AddTicks(receipt.ActualFirstPacketUtcNanoseconds / 100) });
        }
        catch (RpcException error)
        { return Results.Problem(title: "Q65 transmit failed", detail: error.Status.Detail, statusCode: 409); }
        finally { lifetime.Cancel(); }
    }

    internal static long TargetUtc(int seconds, int startSamples12k, bool odd)
    {
        long now = (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;
        long delay = long.TryParse(Environment.GetEnvironmentVariable("SKYROOF_Q65_TX_DELAY_MS"), out long ms) ? ms : 0;
        long slot = Q65SlotTiming.Select(now, seconds, startSamples12k, delay, odd);
        return Q65SlotTiming.Target(slot, seconds, startSamples12k, delay);
    }
}
