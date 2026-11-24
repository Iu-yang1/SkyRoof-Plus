using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkyRoof.Remote.Station;

internal static partial class CatalogOnlineFetcher
{
    private static async Task<T[]> GetRowsAsync<T>(HttpClient http, Uri initial, CancellationToken ct)
    {
        var result = new List<T>();
        var visited = new HashSet<Uri>();
        Uri? next = initial;
        while (next is not null)
        {
            if (!visited.Add(next) || next.Scheme != initial.Scheme || next.Authority != initial.Authority)
                throw new InvalidDataException("Invalid SatNOGS pagination link.");
            using var response = await http.GetAsync(next, ct);
            response.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var root = page.RootElement;
            string? following = null;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("next", out var pointer) && pointer.ValueKind == JsonValueKind.String)
                    following = pointer.GetString();
                root = root.GetProperty("results");
            }
            result.AddRange(root.Deserialize<T[]>(JsonOptions) ??
                throw new InvalidDataException("Invalid SatNOGS response."));
            if (following is null && response.Headers.TryGetValues("Link", out var links))
                following = NextLink(links);
            next = string.IsNullOrEmpty(following) ? null : new Uri(next, following);
        }
        if (result.Count == 0) throw new InvalidDataException("Empty SatNOGS response.");
        return result.ToArray();
    }

    private static string? NextLink(IEnumerable<string> headers)
    {
        foreach (string header in headers)
        {
            var match = Regex.Match(header, "<([^>]+)>;\\s*rel=\"?next\"?", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }
}
