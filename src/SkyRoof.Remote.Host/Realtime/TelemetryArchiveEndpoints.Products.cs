using System.Text.RegularExpressions;
using SkyRoof.Remote.Foundation.Signals;

namespace SkyRoof.Remote.Host.Realtime;

internal static partial class TelemetryArchiveEndpoints
{
    [GeneratedRegex(@"\A[0-9]{19}-[0-9a-f]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProductCursor();

    private static async Task<IResult> ProductListAsync(int norad, string day, string? before, int? limit, HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        if (norad <= 0 || !ValidDay(day) || limit is < 1 or > 100 ||
            (before is not null && !ProductCursor().IsMatch(before)))
            return Results.BadRequest();
        try
        {
            int count = limit ?? 30;
            var items = ReadProducts(norad, day, before).Take(count + 1).ToArray();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { norad, day, items = items.Take(count),
                next = items.Length > count ? TelemetryProductStore.Cursor(items[count - 1]) : null });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Results.Problem(title: "Media catalog unavailable", statusCode: 503); }
    }

    private static IEnumerable<TelemetryProductRecord> ReadProducts(int norad, string day, string? before)
    {
        string folder = TelemetryProductStore.CatalogPath(Root(), norad, day);
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            if (!ProductCursor().IsMatch(stem) || (before is not null && string.CompareOrdinal(stem, before) >= 0))
                continue;
            var record = TelemetryProductStore.Read(file);
            if (record is null || record.Norad != norad || record.Utc.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) != day ||
                TelemetryProductStore.Cursor(record) != stem || !TelemetryProductStore.ValidFormat(record.Format)) continue;
            var product = new FileInfo(TelemetryProductStore.ProductPath(Root(), record.Id, record.Format));
            if (product.Exists && product.Length == record.Bytes) yield return record;
        }
    }
}
