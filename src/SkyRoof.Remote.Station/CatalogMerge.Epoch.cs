using System.Globalization;

namespace SkyRoof.Remote.Station;

internal static partial class CatalogMerge
{
    internal static CatalogCacheTle? Newest(CatalogCacheTle? first, CatalogCacheTle? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return Epoch(second) >= Epoch(first) ? second : first;
    }

    private static DateTimeOffset Epoch(CatalogCacheTle entry)
    {
        if (entry.ReadOmm() is { } mean) return mean.EpochUtc;
        string line = entry.Tle1;
        if (line.Length >= 32 && int.TryParse(line.AsSpan(18, 2), NumberStyles.None,
                CultureInfo.InvariantCulture, out int year) &&
            double.TryParse(line.AsSpan(20, 12), NumberStyles.Float, CultureInfo.InvariantCulture, out double day) &&
            day >= 1 && day < 367)
            return new DateTimeOffset(year < 57 ? 2000 + year : 1900 + year, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddDays(day - 1);
        return entry.Updated ?? DateTimeOffset.MinValue;
    }
}
