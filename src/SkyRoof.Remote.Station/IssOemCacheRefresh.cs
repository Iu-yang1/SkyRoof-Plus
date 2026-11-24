using SkyRoof.Orbit.Orekit;

namespace SkyRoof.Remote.Station;

internal static class IssOemCacheRefresh
{
    internal static async Task RefreshAsync(HttpClient client, Uri source, string target,
        DateTimeOffset utc, CancellationToken ct)
    {
        if (source.Scheme is not ("http" or "https"))
            throw new ArgumentException("ISS OEM source must be HTTP(S).");
        string path = Path.GetFullPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var remote = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await remote.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
            }
            var candidate = new OrekitIssOemTrack(temp);
            if (!candidate.HasFutureCoverage(utc))
                throw new InvalidDataException("Downloaded ISS OEM is entirely expired.");
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
