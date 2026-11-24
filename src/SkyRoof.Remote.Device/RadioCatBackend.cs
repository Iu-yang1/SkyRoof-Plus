using System.Globalization;

namespace SkyRoof.Remote.Device;

public enum CatDialect { SkyCat, Rigctld, IcomLanSatellite }

public sealed class RadioCatBackend(RigctldLink link, CatDialect dialect) : IStationRadioCat
{
    public bool SupportsDuplexFrequencies => dialect == CatDialect.SkyCat;
    public Task<long> ReadRxFrequencyAsync(CancellationToken ct) => link.ReadLongAsync("f", ct);
    public Task<long> ReadTxFrequencyAsync(CancellationToken ct) =>
        link.ReadLongAsync(dialect == CatDialect.SkyCat ? "i" : "f", ct);
    public async Task<bool> ReadPttAsync(CancellationToken ct)
    {
        var readback = await link.ReadLongAsync("t", ct);
        return readback switch
        {
            0 => false,
            1 => true,
            _ => throw new IOException("Invalid CAT PTT readback: " + readback)
        };
    }
    public Task SetRxFrequencyAsync(long hz, CancellationToken ct) =>
        link.WriteAsync("F " + hz.ToString(CultureInfo.InvariantCulture), ct);
    public Task SetTxFrequencyAsync(long hz, CancellationToken ct) =>
        link.WriteAsync((dialect == CatDialect.SkyCat ? "I " : "F ") +
            hz.ToString(CultureInfo.InvariantCulture), ct);
    public async Task<string> ReadModeAsync(bool uplink, CancellationToken ct)
    {
        var value = await link.ReadModeAsync(uplink && dialect == CatDialect.SkyCat ? "x" : "m", ct);
        return value.ToUpperInvariant() switch
        {
            "PKTUSB" => "USB-D", "PKTLSB" => "LSB-D", "PKTFM" => "FM-D",
            "PKTAM" => "AM-D", var mode => mode
        };
    }

    private static string ModeToken(string mode) => mode.ToUpperInvariant() switch
    {
        "USB-D" => "PKTUSB", "LSB-D" => "PKTLSB", "FM-D" => "PKTFM",
        "AM-D" => "PKTAM", var value => value
    };

    public Task SetRxModeAsync(string mode, CancellationToken ct) =>
        link.WriteAsync("M " + ModeToken(mode) + " 0", ct);
    public Task SetTxModeAsync(string mode, CancellationToken ct) =>
        link.WriteAsync((dialect == CatDialect.SkyCat ? "X " : "M ") + ModeToken(mode) + " 0", ct);
    public Task SetPttAsync(bool on, CancellationToken ct) =>
        link.WriteAsync(on ? "T 1" : "T 0", ct);
}
