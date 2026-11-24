using System.Security.Claims;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed partial class Ft4AutoSession
{
    private async Task<TimedTxReceipt> TransmitAsync(Ft4QsoSequence qso, long slot)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Stop);
        var waveform = await Dsp.EncodeFt4Async(new Ft4EncodeRequest
        {
            Message = qso.Message ?? throw new InvalidOperationException("No FT4 message."),
            AudioHz = Request.AudioHz, Gain = Request.Gain
        }, lifetime.Token);
        if (waveform.SampleRate != 48000) throw new InvalidDataException("Invalid FT4 waveform sample rate.");
        long target = TimedFt4Endpoints.TargetForSlot(slot);
        if (target - NowNs < 1_000_000_000)
            throw new InvalidOperationException("FT4 encode or transport missed the target slot; no replay.");
        var tx = new TimedTxRequest
        {
            JobId = Guid.NewGuid().ToString("N"), SessionId = "ft4-auto:" + Context.TraceIdentifier,
            AuthorizedPrincipal = Context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            FrequencyHz = Request.FrequencyHz, Mode = "USB-D", TargetUtcNanoseconds = target,
            Pcm16 = waveform.Pcm16, Sha256 = waveform.Sha256
        };
        try
        {
            return await TimedFt4Endpoints.AwaitAuthorizedAsync(Context,
                Station.RunTimedTxAsync(tx, lifetime.Token), lifetime);
        }
        finally { lifetime.Cancel(); }
    }

}
