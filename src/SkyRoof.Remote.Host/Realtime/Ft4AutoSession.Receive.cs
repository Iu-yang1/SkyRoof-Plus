using System.Text.Json;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed partial class Ft4AutoSession
{
    private async Task<IReadOnlyList<Ft4DecodedContact>?> WaitReceiveAsync(NamedSignalRing ring,
        long expectedSlot, long nextTxSlot)
    {
        // Device requires 1 s to arm (800 ms PTT + 200 ms preparation).
        // Leave additional time for encoding and IPC; skip a missed RX slot, never replay it.
        long deadline = TimedFt4Endpoints.TargetForSlot(nextTxSlot) - 1_250_000_000;
        long nextAuth = 0;
        while (NowNs < deadline)
        {
            if (NowNs >= nextAuth)
            {
                await EnsureAuthorizedAsync();
                nextAuth = NowNs + 1_000_000_000;
            }
            while (ring.TryReadNext(_cursor, out var frame) && frame is not null)
            {
                using var json = JsonDocument.Parse(frame.Payload);
                var root = json.RootElement;
                long slot = root.GetProperty("utc").GetDateTimeOffset().ToUnixTimeMilliseconds() / 7500;
                if (slot < expectedSlot) continue;
                if (slot > expectedSlot) return null;
                ulong epoch = root.GetProperty("sourceEpoch").GetUInt64();
                if (_sourceEpoch.HasValue && _sourceEpoch.Value != epoch)
                    throw new InvalidOperationException("FT4 RX source changed during QSO.");
                _sourceEpoch = epoch;
                return root.GetProperty("messages").EnumerateArray()
                    .Select(x => Ft4NativeContactParser.Parse(x.GetString() ?? ""))
                    .OfType<Ft4DecodedContact>().ToArray();
            }
            await Task.Delay(40, Stop);
        }
        return null;
    }
}
