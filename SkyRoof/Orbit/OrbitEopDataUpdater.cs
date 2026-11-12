using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkyRoof;

internal sealed record OrbitEopUpdateResult(
    string SourceUrl, string LocalFile,
    long SizeBytes, string Sha256, DateTimeOffset DownloadedUtc);

internal static class OrbitEopDataUpdater
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    internal static async Task<OrbitEopUpdateResult> DownloadAsync(
        OrbitSourceSettings settings, CancellationToken cancellationToken = default)
    {
        string directory = OrbitBackendRegistry.ResolveDataDirectory(settings.OrekitDataDirectory);
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "finals2000A.all");
        string staging = target + ".download";
        Exception? lastFailure = null;

        foreach (string url in SatnogsDb.SplitSourceList(settings.OrekitEopSources))
        {
            try
            {
                using var response = await Client.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var output = new FileStream(
                    staging, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await input.CopyToAsync(output, cancellationToken);

                // A partial HTTP download or an HTML error page must not replace
                // previously working EOP records.
                var info = new FileInfo(staging);
                if (info.Length < 500_000 || !LooksLikeIersFinals(staging))
                    throw new InvalidDataException(
                        "Downloaded EOP data is incomplete or not an IERS finals2000A file.");

                string hash = Convert.ToHexString(SHA256.HashData(
                    await File.ReadAllBytesAsync(staging, cancellationToken))).ToLowerInvariant();
                File.Move(staging, target, overwrite: true);

                var record = new OrbitEopUpdateResult(
                    url, target, info.Length, hash, DateTimeOffset.UtcNow);
                string manifest = JsonSerializer.Serialize(record, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(
                    Path.Combine(directory, "finals2000A.manifest.json"), manifest, cancellationToken);
                return record;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                lastFailure = ex;
                if (File.Exists(staging))
                    File.Delete(staging);
            }
        }

        throw new IOException(
            "Unable to retrieve a valid IERS Earth orientation data file from the official sources.",
            lastFailure);
    }

    private static bool LooksLikeIersFinals(string path)
    {
        var pattern = new Regex(
            @"^\s*\d{2}\s+\d{1,2}\s+\d{1,2}\s+\d{5}\.\d+",
            RegexOptions.CultureInvariant);
        int rows = 0;
        foreach (string line in File.ReadLines(path))
            if (pattern.IsMatch(line))
                rows++;
        return rows > 1000;
    }
}
