using System.Text.Json;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed partial class Q65AutoSession(HttpContext context, Q65AutoRequest request,
    DspWaveformBridge dsp, StationDeviceBridge station)
{
    private readonly SignalCursor _cursor = new();
    private ulong? _sourceEpoch, _resultEpoch;
    private CancellationToken Stop => context.RequestAborted;
    private static long NowNs => (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;

    internal async Task RunAsync(Ft4QsoSequence qso, Q65RxConfiguration config, NamedSignalRing ring)
    {
        long next = 0;
        while (!qso.Finished)
        {
            await EnsureAuthorizedAsync();
            if (_resultEpoch.HasValue && ring.StreamEpoch != _resultEpoch.Value)
                throw new InvalidOperationException("Q65 receiver generation changed before TX.");
            var encoded = await EncodeAsync(qso, config);
            long delay = Q65TxDelayMs();
            long slot = Q65SlotTiming.Select(NowNs, encoded.SlotSeconds, encoded.TxStartSamples12K, delay, request.Odd, next);
            if (next > 0 && slot > next) await PublishAsync(qso, "tx-slot-deferred", slot, encoded.SlotSeconds);
            await PublishAsync(qso, "transmitting", slot, encoded.SlotSeconds);
            var receipt = await TransmitAsync(encoded, slot, delay);
            if (receipt.State != "completed") throw new InvalidOperationException("Timed Q65 stopped: " + receipt.State);
            qso.Sent();
            await PublishAsync(qso, "sent", slot, encoded.SlotSeconds);
            next = slot + 2;
            if (qso.Finished) break;
            next = await ReceiveAsync(qso, config, ring, slot + 1, encoded.SlotSeconds);
        }
        await PublishAsync(qso, qso.Stage == Ft4Exchange.Complete ? "completed" : "attempts-exhausted", next, 0);
    }

    private async Task<long> ReceiveAsync(Ft4QsoSequence qso, Q65RxConfiguration config, NamedSignalRing ring, long slot, int seconds)
    {
        for (int misses = 0; misses < request.Attempts; misses++, slot += 2)
        {
            var window = await WaitReceiveAsync(ring, config, slot, seconds);
            if (window is not null)
            {
                qso.Observe(window);
                qso.FinishReceiveWindow();
                return slot + 1;
            }
            await PublishAsync(qso, "rx-window-missed", slot, seconds);
        }
        throw new InvalidOperationException("No complete Q65 receive window; automatic transmission ended.");
    }

    private async Task EnsureAuthorizedAsync()
    {
        Stop.ThrowIfCancellationRequested();
        if (!await TimedFt4Endpoints.IsOperatorAsync(context))
            throw new InvalidOperationException("Q65 operator authorization revoked.");
    }

    private async Task PublishAsync(Ft4QsoSequence qso, string state, long slot, int seconds)
    {
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            state, slotIndex = slot, slotSeconds = seconds, stage = qso.Stage.ToString(),
            peer = qso.Peer, message = qso.Message, grid = qso.PeerGrid, qso.ReportSent, qso.ReportReceived
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n", Stop);
        await context.Response.Body.FlushAsync(Stop);
    }
}
