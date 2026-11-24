using System.Text.Json;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed partial class Ft4AutoSession(HttpContext context, Ft4AutoRequest request,
    DspWaveformBridge dsp, StationDeviceBridge station)
{
    private HttpContext Context => context;
    private Ft4AutoRequest Request => request;
    private DspWaveformBridge Dsp => dsp;
    private StationDeviceBridge Station => station;

    private const long SlotNs = 7_500_000_000;
    private readonly SignalCursor _cursor = new();
    private ulong? _sourceEpoch;
    private CancellationToken Stop => context.RequestAborted;
    private static long NowNs => (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;

    public async Task RunAsync(Ft4QsoSequence qso, NamedSignalRing ring)
    {
        long slot = (NowNs + 2_000_000_000 + SlotNs - 1) / SlotNs;
        if ((slot % 2 == 1) != request.Odd) slot++;
        await PublishAsync(qso, "started", slot);
        int misses = 0;
        while (!qso.Finished)
        {
            await EnsureAuthorizedAsync();
            await PublishAsync(qso, "transmitting", slot);
            var receipt = await TransmitAsync(qso, slot);
            if (receipt.State != "completed")
                throw new InvalidOperationException("Timed FT4 stopped: " + receipt.State);
            qso.Sent();
            await PublishAsync(qso, "sent", slot);
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
                await PublishAsync(qso, "rx-window-missed", slot);
                if (misses >= request.Attempts)
                    throw new InvalidOperationException("No timely complete FT4 RX windows; automatic TX ended.");
            } while (true);
        }
        await PublishAsync(qso, qso.Stage == Ft4Exchange.Complete ? "completed" : "attempts-exhausted", slot);
    }

    private async Task EnsureAuthorizedAsync()
    {
        Stop.ThrowIfCancellationRequested();
        if (!await TimedFt4Endpoints.IsOperatorAsync(context))
            throw new InvalidOperationException("FT4 operator authorization revoked.");
    }

    private Task PublishAsync(Ft4QsoSequence qso, string state, long slotIndex) => WriteAsync(context, new
    {
        state, slotIndex, stage = qso.Stage.ToString(), peer = qso.Peer, message = qso.Message,
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
