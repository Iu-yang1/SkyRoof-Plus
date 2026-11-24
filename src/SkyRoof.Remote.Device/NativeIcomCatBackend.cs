namespace SkyRoof.Remote.Device;

internal sealed class NativeIcomCatBackend(NativeIcomCatSession session) : IStationRadioCat
{
    public bool SupportsDuplexFrequencies => true;
    public Task<long> ReadRxFrequencyAsync(CancellationToken ct) =>
        session.Current.ReadFrequencyAsync(false, ct);
    public Task<long> ReadTxFrequencyAsync(CancellationToken ct) =>
        session.Current.ReadFrequencyAsync(true, ct);
    public Task<bool> ReadPttAsync(CancellationToken ct) =>
        session.Current.ReadPttAsync(ct);
    public Task SetRxFrequencyAsync(long hz, CancellationToken ct) =>
        session.Current.SetFrequencyAsync(false, hz, ct);
    public Task SetTxFrequencyAsync(long hz, CancellationToken ct) =>
        session.Current.SetFrequencyAsync(true, hz, ct);
    public Task SetRxModeAsync(string mode, CancellationToken ct) =>
        session.Current.SetModeAsync(false, mode, ct);
    public Task SetTxModeAsync(string mode, CancellationToken ct) =>
        session.Current.SetModeAsync(true, mode, ct);
    public Task SetPttAsync(bool on, CancellationToken ct) =>
        session.Current.SetPttAsync(on, ct);
}
