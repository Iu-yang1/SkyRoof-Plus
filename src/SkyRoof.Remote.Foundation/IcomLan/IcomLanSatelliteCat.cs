namespace SkyRoof.Remote.Foundation.IcomLan;

/// <summary>IC-9700 SAT mode: MAIN downlink, SUB uplink. Never use 25/26 in SAT mode.</summary>
public sealed class IcomLanSatelliteCat(IcomLanCivSocket socket, byte radioAddress)
{
    private readonly IcomLanCivControl _control = new(socket, radioAddress);
    private readonly IcomLanCivReadbacks _readbacks = new(socket, radioAddress);
    private readonly SemaphoreSlim _bandGate = new(1, 1);

    public Task<long> ReadFrequencyAsync(bool uplink, CancellationToken ct) =>
        OnBandAsync(uplink, _readbacks.ReadSelectedFrequencyAsync, ct);

    public Task SetFrequencyAsync(bool uplink, long hz, CancellationToken ct) =>
        OnBandAsync(uplink, async token =>
        {
            await _control.SetSelectedFrequencyAsync(hz, token);
            return true;
        }, ct);

    public Task SetModeAsync(bool uplink, string mode, CancellationToken ct) =>
        OnBandAsync(uplink, async token =>
        {
            await _control.SetSelectedModeAsync(new(IcomCivMode.ParseCode(mode), 1), token);
            return true;
        }, ct);

    public Task<bool> ReadPttAsync(CancellationToken ct) => _readbacks.ReadPttAsync(ct);

    public Task SetPttAsync(bool on, CancellationToken ct) => on
        ? OnBandAsync(false, async token =>
        {
            await _control.SetPttAsync(true, token);
            return true;
        }, ct)
        : _control.SetPttAsync(false, ct);

    private async Task<T> OnBandAsync<T>(bool uplink,
        Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await _bandGate.WaitAsync(ct);
        try
        {
            byte[] mode = await socket.QueryAsync(new(radioAddress, 0xE0, [0x16, 0x5A], 1), ct);
            if (mode[0] != 1)
                throw new InvalidOperationException("IC-9700 is not in SATELLITE mode.");
            await _control.SelectBandAsync(uplink, ct);
            try { return await operation(ct); }
            finally
            {
                if (uplink && !socket.QueryFailureTask.IsCompleted)
                    await _control.SelectBandAsync(false, ct);
            }
        }
        finally { _bandGate.Release(); }
    }
}
