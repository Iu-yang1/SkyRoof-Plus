using System.Security.Claims;
using System.Text.Json;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed partial class Ft4AutoSession(HttpContext context, Ft4AutoRequest request,
    DspWaveformBridge dsp, StationDeviceBridge station)
{
    private const long SlotNs = 7_500_000_000;
    private readonly SignalCursor _cursor = new();
    private ulong? _sourceEpoch;
    private CancellationToken Stop => context.RequestAborted;
    private static long NowNs => (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;

    public async Task RunAsync(Ft4QsoSequence qso, NamedSignalRing ring)
    {
        long slot = (NowNs + 2_000_000_000 + SlotNs - 1) / SlotNs;
        if ((slot % 2 == 1) != request.Odd) slot++;
        await PublishAsync(qso, "started");
        int misses = 0;
        while (!qso.Finished)
        {
            await EnsureAuthorizedAsync();
            await PublishAsync(qso, "transmitting");
            var receipt = await TransmitAsync(qso, slot);
            if (receipt.State != "completed")
                throw new InvalidOperationException("Timed FT4 stopped: " + receipt.State);
            qso.Sent();
            await PublishAsync(qso, "sent");
            if (qso.Finished) break;
            do
            {
                slot += 2;
                var window = await WaitReceiveAsync(ring, slot - 1, slot);
                if (window is not null)
                {
                    qso.Observe(window);
                    qso.FinishReceiveWindow();
                    misses = 0;
                    break;
                }
                misses++;
                await PublishAsync(qso, "rx-window-missed");
                if (misses >= request.Attempts)
                    throw new InvalidOperationException("No timely complete FT4 RX windows; automatic TX ended.");
            } while (true);
        }
        await PublishAsync(qso, qso.Stage == Ft4Exchange.Complete ? "completed" : "attempts-exhausted");
    }

    private async Task<TimedTxReceipt> TransmitAsync(Ft4QsoSequence qso, long slot)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Stop);
        var waveform = await dsp.EncodeFt4Async(new Ft4EncodeRequest
        {
            Message = qso.Message ?? throw new InvalidOperationException("No FT4 message."),
            AudioHz = request.AudioHz, Gain = request.Gain
        }, lifetime.Token);
        if (waveform.SampleRate != 48000) throw new InvalidDataException("Invalid FT4 waveform sample rate.");
        long target = TimedFt4Endpoints.TargetForSlot(slot);
        if (target - NowNs < 1_000_000_000)
            throw new InvalidOperationException("FT4 encode or transport missed the target slot; no replay.");
        var tx = new TimedTxRequest
        {
            JobId = Guid.NewGuid().ToString("N"), SessionId = "ft4-auto:" + context.TraceIdentifier,
            AuthorizedPrincipal = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            FrequencyHz = request.FrequencyHz, Mode = "USB-D", TargetUtcNanoseconds = target,
            Pcm16 = waveform.Pcm16, Sha256 = waveform.Sha256
        };
        try
        {
            return await TimedFt4Endpoints.AwaitAuthorizedAsync(context,
                station.RunTimedTxAsync(tx, lifetime.Token), lifetime);
        }
        finally { lifetime.Cancel(); }
    }

    private async Task EnsureAuthorizedAsync()
    {
        Stop.ThrowIfCancellationRequested();
        if (!await TimedFt4Endpoints.IsOperatorAsync(context))
            throw new InvalidOperationException("FT4 operator authorization revoked.");
    }

    private Task PublishAsync(Ft4QsoSequence qso, string state) => WriteAsync(context, new
    {
        state, stage = qso.Stage.ToString(), peer = qso.Peer, message = qso.Message,
        grid = qso.PeerGrid, reportSent = qso.ReportSent, reportReceived = qso.ReportReceived
    });

    internal static Task WriteErrorAsync(HttpContext context, string error) =>
        WriteAsync(context, new { state = "error", error });

    private static async Task WriteAsync(HttpContext context, object value)
    {
        await context.Response.WriteAsync(JsonSerializer.Serialize(value) + "\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
}
