using SkyRoof.CW;
using SkyRoof.Remote.Foundation.IcomLan;

namespace SkyRoof.Remote.Device;

internal sealed class NativeCwKeyer(IcomLanSatelliteCat cat) : ICwKeyerSession
{
    public async Task<CwKeyerStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var value = await cat.ReadCwAsync(ct);
        return new("IDLE", value.Mode == "CWR" ? "CW-R" : value.Mode,
            value.BreakIn, value.Transmitting, value.KeySpeedRaw, value.FrequencyHz);
    }

    public async Task<CwKeySpeedResult> SetWpmAsync(double wpm, CancellationToken ct = default)
    {
        int raw = await cat.SetCwKeySpeedAsync(CwMessageTiming.WpmToRawKeySpeed(wpm), ct);
        return new(raw, CwMessageTiming.RawKeySpeedToWpm(raw));
    }

    public Task SendAsync(string text, CancellationToken ct = default)
    {
        CwMessageTiming.ValidateText(text);
        return cat.SendCwTextAsync(text, ct);
    }

    public async Task SendGuardedAsync(long expectedTxFrequencyHz, int toleranceHz, string text,
        CancellationToken ct = default)
    {
        var status = await GetStatusAsync(ct);
        if (status.ActualTxFrequencyHz is not long hz || Math.Abs(hz - expectedTxFrequencyHz) > toleranceHz)
            throw new InvalidOperationException("CW TX frequency changed.");
        await SendAsync(text, ct);
    }

    public Task StopAsync(CancellationToken ct = default) => cat.StopCwTextAsync(ct);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
