using System.Text;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed record IcomCwReadback(string Mode, int BreakIn, int KeySpeedRaw, long FrequencyHz, bool Transmitting);

public sealed partial class IcomLanSatelliteCat
{
    public Task<IcomCwReadback> ReadCwAsync(CancellationToken ct) =>
        OnBandAsync(true, async token =>
        {
            var mode = await _readbacks.ReadSelectedModeAsync(token);
            var bk = await CivSocket.QueryAsync(new(RadioAddress, 0xE0, [0x16, 0x47], 1), token);
            if (bk[0] > 2) throw new InvalidDataException("Invalid BK-IN readback.");
            return new IcomCwReadback(mode.ToModeName(false), bk[0], await ReadKeySpeedAsync(token),
                await _readbacks.ReadSelectedFrequencyAsync(token), await _readbacks.ReadPttAsync(token));
        }, ct);

    public Task<int> SetCwKeySpeedAsync(int raw, CancellationToken ct) =>
        OnBandAsync(true, async token =>
        {
            if (raw is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(raw));
            await _control.WriteAsync([0x14, 0x0C, (byte)(raw / 100),
                (byte)(((raw / 10) % 10 << 4) | raw % 10)], token);
            int actual = await ReadKeySpeedAsync(token);
            if (actual != raw) throw new IOException("CW key speed readback mismatch.");
            return actual;
        }, ct);

    public Task SendCwTextAsync(string text, CancellationToken ct)
    {
        if (text.Length is < 1 or > 30 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && !" /?.-,:'()=+\"@^".Contains(c)))
            throw new ArgumentException("Unsupported CW keyer text.");
        return OnBandAsync(true, async token =>
        {
            var mode = await _readbacks.ReadSelectedModeAsync(token);
            var bk = await CivSocket.QueryAsync(new(RadioAddress, 0xE0, [0x16, 0x47], 1), token);
            if (mode.Code is not (3 or 7) || bk[0] is not (1 or 2) || await _readbacks.ReadPttAsync(token))
                throw new InvalidOperationException("CW requires idle TX VFO in CW/CW-R with BK-IN.");
            await _control.WriteAsync([0x17, .. Encoding.ASCII.GetBytes(text)], token);
            return true;
        }, ct);
    }

    public Task StopCwTextAsync(CancellationToken ct) =>
        OnBandAsync(true, async token =>
        {
            await _control.WriteAsync([0x17, 0xFF], token);
            return true;
        }, ct);

    private async Task<int> ReadKeySpeedAsync(CancellationToken ct)
    {
        byte[] data = await CivSocket.QueryAsync(new(RadioAddress, 0xE0, [0x14, 0x0C], 2), ct);
        int raw = data[0] * 100 + (data[1] >> 4) * 10 + (data[1] & 15);
        if (data[0] > 2 || (data[1] >> 4) > 9 || (data[1] & 15) > 9 || raw > 255)
            throw new InvalidDataException("Invalid CW key speed BCD.");
        return raw;
    }
}
