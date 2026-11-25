using System.Security.Claims;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed partial class Q65AutoSession
{
    private static long Q65TxDelayMs() =>
        long.TryParse(Environment.GetEnvironmentVariable("SKYROOF_Q65_TX_DELAY_MS"), out long ms) ? ms : 0;

    private async Task<Q65EncodedWaveform> EncodeAsync(Ft4QsoSequence qso, Q65RxConfiguration config)
    {
        var result = await dsp.EncodeQ65Async(new Q65EncodeRequest
        {
            Submode = config.Submode, Message = qso.Message ?? throw new InvalidOperationException("No Q65 message."),
            AudioHz = config.RxHz, Gain = request.Gain
        }, Stop);
        if (result.Waveform is not { SampleRate: 48000 } || result.SlotSeconds is not (15 or 30 or 60 or 120 or 300))
            throw new InvalidDataException("Invalid Q65 DSP waveform.");
        return result;
    }

    private async Task<TimedTxReceipt> TransmitAsync(Q65EncodedWaveform encoded, long slot, long delay)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Stop);
        var waveform = encoded.Waveform;
        long target = Q65SlotTiming.Target(slot, encoded.SlotSeconds, encoded.TxStartSamples12K, delay);
        long frequency = await Q65TrackingFrequency.ResolveAsync(station, request.TrackingJobId, request.FrequencyHz, target, Stop);
        var job = new TimedTxRequest
        {
            JobId = Guid.NewGuid().ToString("N"), SessionId = "q65-auto:" + context.TraceIdentifier,
            AuthorizedPrincipal = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            FrequencyHz = frequency, Mode = "USB-D", TargetUtcNanoseconds = target,
            Pcm16 = waveform.Pcm16, Sha256 = waveform.Sha256
        };
        try
        {
            return await TimedFt4Endpoints.AwaitAuthorizedAsync(context,
                station.RunTimedTxAsync(job, lifetime.Token), lifetime);
        }
        finally { lifetime.Cancel(); }
    }
}
