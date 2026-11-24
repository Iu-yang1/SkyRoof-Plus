namespace SkyRoof.Remote.Device;

public interface IStationRadioCat
{
    bool SupportsDuplexFrequencies { get; }
    Task<long> ReadRxFrequencyAsync(CancellationToken ct);
    Task<long> ReadTxFrequencyAsync(CancellationToken ct);
    Task<bool> ReadPttAsync(CancellationToken ct);
    Task SetRxFrequencyAsync(long hz, CancellationToken ct);
    Task SetTxFrequencyAsync(long hz, CancellationToken ct);
    Task SetRxModeAsync(string mode, CancellationToken ct);
    Task SetTxModeAsync(string mode, CancellationToken ct);
    Task SetPttAsync(bool on, CancellationToken ct);
}
