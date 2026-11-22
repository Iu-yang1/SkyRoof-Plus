using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkyRoof.Remote.Station;

/// <summary>
/// Refreshes IERS Earth orientation data before the Orekit process-wide context is initialized.
/// Last known-good files are preserved when the network or a source is unavailable.
/// </summary>
internal static class StationOrekitEopBootstrap
{
    private const string DefaultSources =
        "https://maia.usno.navy.mil/ser7/finals2000A.all;" +
        "https://datacenter.iers.org/products/eop/rapid/standard/finals2000A.all";
    private static readonly Regex FinalsRow = new(
        @"^\s*\d{2}\s+\d{1,2}\s+\d{1,2}\s+\d{5}\.\d+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static async Task RefreshAsync(string directory, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(directory, "finals2000A.all");
        if (File.Exists(target) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(target) < TimeSpan.FromDays(7))
            return;
        var configured = Environment.GetEnvironmentVariable("SKYROOF_OREKIT_EOP_AUTO");
        if (string.Equals(configured, "false", StringComparison.OrdinalIgnoreCase))
            return;

        var sources = (Environment.GetEnvironmentVariable("SKYROOF_OREKIT_EOP_SOURCES")
            ?? DefaultSources).Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        Directory.CreateDirectory(directory);
        foreach (var source in sources)
        {
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                if (Uri.TryCreate(source, UriKind.Absolute, out var url) &&
                    url.Scheme is "http" or "https")
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                    using var response = await client.GetAsync(url,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var output = new FileStream(temp, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None);
                    await remote.CopyToAsync(output, cancellationToken);
                }
                else
                {
                    File.Copy(Environment.ExpandEnvironmentVariables(source), temp);
                }
                var info = new FileInfo(temp);
                if (info.Length < 500_000 || !LooksLikeFinals(temp))
                    throw new InvalidDataException("Invalid or incomplete IERS finals2000A data.");
                var sha256 = Convert.ToHexString(SHA256.HashData(
                    await File.ReadAllBytesAsync(temp, cancellationToken))).ToLowerInvariant();
                File.Move(temp, target, overwrite: true);
                await File.WriteAllTextAsync(
                    Path.Combine(directory, "finals2000A.manifest.json"),
                    JsonSerializer.Serialize(new
                    {
                        source, sha256, bytes = info.Length, downloadedUtc = DateTimeOffset.UtcNow
                    }),
                    cancellationToken);
                logger.LogInformation("IERS EOP updated from {Source}, SHA256={Hash}", source, sha256);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                logger.LogWarning(ex, "IERS EOP source {Source} was unavailable; trying next.", source);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        logger.LogWarning("No valid IERS EOP source available; retaining previous cached data.");
    }

    private static bool LooksLikeFinals(string path)
    {
        var rows = 0;
        foreach (var line in File.ReadLines(path))
            if (FinalsRow.IsMatch(line) && ++rows > 1000) return true;
        return false;
    }
}
