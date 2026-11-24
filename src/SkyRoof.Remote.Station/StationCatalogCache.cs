using System.Globalization;
using System.Text.Json;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class StationCatalogCache
{
    private readonly object _gate = new();
    private readonly string _path = Environment.GetEnvironmentVariable("SKYROOF_SATELLITE_CATALOG") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyRoof", "Remote", "Satellites.json");
    private (DateTime Modified, long Length)? _version;
    private CatalogCacheSatellite[] _entries = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal CatalogSatellite[] Search(string query, int limit)
    {
        lock (_gate)
        {
            Reload();
            var utc = DateTimeOffset.UtcNow;
            return _entries.Where(s => Matches(s, query.Trim()))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit <= 0 ? 50 : limit, 1, 200))
                .Select(s => Map(s, utc)).ToArray();
        }
    }

    private void Reload()
    {
        var info = new FileInfo(_path);
        var current = (info.LastWriteTimeUtc, info.Length);
        if (_version == current) return;
        var entries = JsonSerializer.Deserialize<CatalogCacheSatellite[]>(
            File.ReadAllBytes(info.FullName), JsonOptions)
            ?? throw new InvalidDataException("Catalog is empty.");
        _entries = entries;
        _version = current;
    }

    private static bool Matches(CatalogCacheSatellite entry, string query) =>
        query.Length == 0 || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (entry.Names?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (entry.AllNames?.Any(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false) ||
        entry.Norad?.ToString(CultureInfo.InvariantCulture) == query;

    private static CatalogSatellite Map(CatalogCacheSatellite entry, DateTimeOffset utc)
    {
        var orbit = entry.OrbitAt(utc);
        var result = new CatalogSatellite
        {
            SatelliteId = entry.SatelliteId, Norad = entry.Norad ?? 0,
            Name = entry.Name, Status = entry.Status,
            TleLine1 = orbit?.HasTle == true ? orbit.Tle1 : "",
            TleLine2 = orbit?.HasTle == true ? orbit.Tle2 : "",
            OrbitSource = orbit?.Source ?? "",
            OrbitState = orbit?.HasTle == true ? "tle" : orbit?.Omm is not null ? "omm-only" : "missing"
        };
        result.Transmitters.AddRange(entry.Transmitters.Select(MapTransmitter));
        return result;
    }

    private static CatalogTransmitter MapTransmitter(CatalogCacheTransmitter tx) => new()
    {
        Id = tx.Id, Description = tx.Description, Alive = tx.Alive, Status = tx.Status ?? "",
        Mode = tx.DownlinkMode ?? tx.Mode ?? "", UplinkMode = tx.UplinkMode ?? "",
        Link = new RadioLinkProfile
        {
            DownlinkLowHz = tx.DownlinkLow ?? 0, DownlinkHighHz = tx.DownlinkHigh ?? 0,
            UplinkLowHz = tx.UplinkLow ?? 0, UplinkHighHz = tx.UplinkHigh ?? 0,
            Inverting = tx.Invert
        }
    };
}
