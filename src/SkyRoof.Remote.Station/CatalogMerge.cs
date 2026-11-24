namespace SkyRoof.Remote.Station;

internal static partial class CatalogMerge
{
    internal static CatalogCacheSatellite[] Combine(CatalogCacheSatellite[] desktop,
        CatalogCacheSatellite[] satnogs, CatalogCacheSatellite[] celestrak)
    {
        var entries = satnogs.ToDictionary(s => s.SatelliteId, StringComparer.Ordinal);
        foreach (var local in desktop)
        {
            if (string.IsNullOrWhiteSpace(local.SatelliteId)) continue;
            var live = entries.GetValueOrDefault(local.SatelliteId);
            if (live is null && local.Norad.HasValue)
            {
                var matches = entries.Values.Where(s => s.Norad == local.Norad).Take(2).ToArray();
                if (matches.Length == 1)
                {
                    live = matches[0];
                    entries.Remove(live.SatelliteId);
                }
            }
            entries[local.SatelliteId] = MergeLocal(local, live);
        }

        foreach (var incoming in celestrak)
        {
            var ids = entries.Values.Where(s => s.Norad.HasValue && s.Norad == incoming.Norad)
                .Select(s => s.SatelliteId).ToArray();
            if (ids.Length == 0) { entries[incoming.SatelliteId] = incoming; continue; }
            foreach (string id in ids)
            {
                var original = entries[id];
                entries[id] = original with
                {
                    AutomaticTle = Newest(Automatic(original), incoming.AutomaticTle)
                };
            }
        }
        return entries.Values.ToArray();
    }

    private static CatalogCacheSatellite MergeLocal(CatalogCacheSatellite local, CatalogCacheSatellite? live)
    {
        if (live is null) return local;
        var transmitters = live.Transmitters.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (var tx in local.Transmitters)
        {
            if (!transmitters.TryGetValue(tx.Id, out var remote) || tx.LocalCustom ||
                tx.Manual is { ValueKind: System.Text.Json.JsonValueKind.Object })
                transmitters[tx.Id] = tx;
            else if (!string.IsNullOrWhiteSpace(tx.DownlinkMode) &&
                string.Equals(tx.Mode, remote.Mode, StringComparison.OrdinalIgnoreCase))
                transmitters[tx.Id] = remote with { DownlinkMode = tx.DownlinkMode };
        }
        return local with
        {
            Status = live.Status, AutomaticTle = Newest(Automatic(local), Automatic(live)),
            Transmitters = transmitters.Values.ToArray()
        };
    }

    private static CatalogCacheTle? Automatic(CatalogCacheSatellite entry) =>
        entry.AutomaticTle ?? (entry.ManualTle is null ? entry.Tle : null);
}
