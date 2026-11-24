using System.Globalization;
using System.Text.Json;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class StationCatalogCache
{
    private readonly object _gate = new();
    private readonly string[] _paths = [CatalogPaths.Desktop, CatalogPaths.Satnogs, CatalogPaths.Celestrak];
    private (DateTime Modified, long Length)[]? _version;
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

    internal CatalogCacheTle ResolveOrbit(string satelliteId, DateTimeOffset utc)
    {
        lock (_gate)
        {
            Reload();
            var satellite = _entries.FirstOrDefault(s => s.SatelliteId == satelliteId)
                ?? throw new ArgumentException("Satellite is no longer present in the station catalog.");
            return satellite.OrbitAt(utc) ??
                throw new ArgumentException("Selected satellite has no usable orbit.");
        }
    }

    private void Reload()
    {
        var current = _paths.Select(VersionOf).ToArray();
        if (_version is not null && current.SequenceEqual(_version)) return;
        var desktop = Read(_paths[0]);
        var satnogs = Read(_paths[1]);
        var celestrak = Read(_paths[2]);
        var entries = CatalogMerge.Combine(desktop, satnogs, celestrak);
        if (entries.Length == 0) throw new InvalidDataException("No station catalog available.");
        _entries = entries;
        _version = current;
    }

    private static (DateTime Modified, long Length) VersionOf(string file)
    {
        var info = new FileInfo(file);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1);
    }

    private static CatalogCacheSatellite[] Read(string file) => File.Exists(file)
        ? JsonSerializer.Deserialize<CatalogCacheSatellite[]>(File.ReadAllBytes(file), JsonOptions) ??
            throw new InvalidDataException("Invalid station catalog.")
        : [];

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
