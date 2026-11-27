using System.Text.Json;
using SGPdotNET.Parsers;

namespace SkyRoof.Remote.Station;

internal static partial class CatalogOnlineFetcher
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<CatalogCacheSatellite[]> FetchSatnogsAsync(HttpClient http, Uri origin, CancellationToken ct)
    {
        var satellites = await GetRowsAsync<CatalogCacheSatellite>(http, new Uri(origin, "satellites/?format=json"), ct);
        var transmitters = await GetRowsAsync<CatalogCacheTransmitter>(http, new Uri(origin, "transmitters/?format=json"), ct);
        var tles = await GetRowsAsync<CatalogCacheTle>(http, new Uri(origin, "tle/?format=json"), ct);
        var byId = transmitters.Where(t => !string.IsNullOrWhiteSpace(t.SatelliteId))
            .ToLookup(t => t.SatelliteId);
        var byNorad = transmitters.Where(t => string.IsNullOrWhiteSpace(t.SatelliteId) && t.Norad.HasValue)
            .ToLookup(t => t.Norad);
        var orbitByNorad = tles.Where(t => t.Norad.HasValue && t.HasTle).ToLookup(t => t.Norad);
        return satellites.Where(s => !string.IsNullOrWhiteSpace(s.SatelliteId)).Select(s => s with
        {
            Transmitters = byId[s.SatelliteId].Concat(byNorad[s.Norad])
                .Where(t => t.DownlinkLow.HasValue || t.UplinkLow.HasValue).DistinctBy(t => t.Id).ToArray(),
            AutomaticTle = orbitByNorad[s.Norad].Aggregate((CatalogCacheTle?)null,
                (best, next) => CatalogMerge.Newest(best, next with { Source = "SatNOGS TLE" }))
        }).ToArray();
    }

    internal static async Task<CatalogCacheSatellite[]> FetchCelestrakAsync(HttpClient http, Uri source, CancellationToken ct)
    {
        string csv = await http.GetStringAsync(source, ct);
        var data = new OmmCsvParser().Parse(csv);
        var result = new List<CatalogCacheSatellite>();
        foreach (var omm in data)
        {
            if (omm.NoradCatID == 0 || omm.MeanMotion <= 0 || omm.Epoch == default) continue;
            int norad = checked((int)omm.NoradCatID);
            var tle = new CatalogCacheTle
            {
                Norad = norad, Source = "CelesTrak OMM CSV",
                Updated = new DateTimeOffset(DateTime.SpecifyKind(omm.Epoch, DateTimeKind.Utc)),
                Omm = JsonSerializer.Deserialize<JsonElement>(Newtonsoft.Json.JsonConvert.SerializeObject(omm))
            };
            _ = tle.ReadOmm();
            result.Add(new CatalogCacheSatellite
            {
                SatelliteId = "celestrak:" + norad, Norad = norad,
                Name = string.IsNullOrWhiteSpace(omm.ObjectName) ? "NORAD " + norad : omm.ObjectName,
                AutomaticTle = tle
            });
        }
        return result.ToArray();
    }

    internal static async Task SaveAsync(string path, CatalogCacheSatellite[] records, CancellationToken ct)
    {
        if (records.Length == 0) throw new InvalidDataException("An empty online catalog cannot replace a valid snapshot.");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new ArgumentException("Cache path requires a parent directory."));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
