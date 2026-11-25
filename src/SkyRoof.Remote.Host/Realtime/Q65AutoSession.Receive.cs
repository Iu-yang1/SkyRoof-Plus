using System.Text.Json;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed partial class Q65AutoSession
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private async Task<IReadOnlyList<Ft4DecodedContact>?> WaitReceiveAsync(NamedSignalRing ring,
        Q65RxConfiguration config, long slot, int seconds)
    {
        long deadline = checked((slot + 1) * seconds * 1_000_000_000L + Math.Max(15, seconds / 2) * 1_000_000_000L);
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
                string? state = root.GetProperty("state").GetString();
                if (_resultEpoch.HasValue && _resultEpoch.Value != frame.StreamEpoch)
                    throw new InvalidOperationException("Q65 result generation changed during automatic QSO.");
                if (state == "error") throw new InvalidOperationException("Q65 receiver error: " + root.GetProperty("error").GetString());
                if (state != "decoded") continue;
                long index = root.GetProperty("utc").GetDateTimeOffset().ToUnixTimeSeconds() / seconds;
                if (index < slot) continue;
                if (index > slot) return null;
                if (root.GetProperty("config").Deserialize<Q65RxConfiguration>(WebJson) is not { } actual || !actual.Equals(config))
                    throw new InvalidOperationException("Q65 receiver configuration changed.");
                ulong epoch = root.GetProperty("sourceEpoch").GetUInt64();
                if (_sourceEpoch.HasValue && _sourceEpoch.Value != epoch)
                    throw new InvalidOperationException("Q65 RX source changed during automatic QSO.");
                _sourceEpoch = epoch;
                _resultEpoch = frame.StreamEpoch;
                return root.GetProperty("messages").EnumerateArray()
                    .Select(ParseContact).OfType<Ft4DecodedContact>().ToArray();
            }
            await Task.Delay(40, Stop);
        }
        return null;
    }

    internal static Ft4DecodedContact? ParseContact(JsonElement row)
    {
        string text = row.GetProperty("text").GetString() ?? "";
        if (!row.GetProperty("snrDb").TryGetDouble(out double snr) || !double.IsFinite(snr)) return null;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] == "CQ" || text.Contains('?') || text.Contains("...", StringComparison.Ordinal)) return null;
        return new(parts[0].Trim('<', '>'), parts[1].Trim('<', '>'), parts[2], (int)Math.Clamp(Math.Round(snr), -50, 50));
    }
}
