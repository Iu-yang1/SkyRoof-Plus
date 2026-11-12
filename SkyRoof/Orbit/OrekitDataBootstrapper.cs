using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace SkyRoof;

/// <summary>
/// Populates the user-selected Orekit data folder before the global Orekit context
/// is initialized. Network errors never overwrite a cached data set.
/// </summary>
internal static class OrekitDataBootstrapper
{
    // Provenance: eSpace-epfl/orekit-data, DE-430-ephemerides/lnxp1990.430,
    // blob 70ec728d3d5018ff3df2eaa4ae439e668de7ef6b.
    private const string De430Source =
        "https://raw.githubusercontent.com/eSpace-epfl/orekit-data/master/DE-430-ephemerides/lnxp1990.430";
    private const string De430BlobSha1 = "70ec728d3d5018ff3df2eaa4ae439e668de7ef6b";
    private const long De430FileSize = 7_459_904;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };

    internal static async Task EnsureAsync(OrbitSourceSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.AutoDownloadOrekitData)
            return;

        string directory = OrbitBackendRegistry.ResolveDataDirectory(settings.OrekitDataDirectory);
        Directory.CreateDirectory(directory);

        string eop = Path.Combine(directory, "finals2000A.all");
        if (!File.Exists(eop) ||
            DateTime.UtcNow - File.GetLastWriteTimeUtc(eop) > TimeSpan.FromDays(7))
        {
            try
            {
                var result = await OrbitEopDataUpdater.DownloadAsync(settings, cancellationToken);
                Log.Information("Orekit IERS EOP downloaded: {File} SHA256={Hash}",
                    result.LocalFile, result.Sha256);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning(ex, "IERS EOP refresh failed; preserving the local dataset.");
            }
        }

        string target = Path.Combine(directory, "lnxp1990.430");
        if (await IsVerifiedDe430Async(target, cancellationToken))
            return;

        string staging = target + ".download";
        try
        {
            using var response = await Client.GetAsync(
                De430Source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, cancellationToken);

            var info = new FileInfo(staging);
            if (info.Length != De430FileSize)
                throw new InvalidDataException("Orekit DE430 ephemeris length does not match the pinned release.");

            // Verify the exact upstream Git blob, not merely its filename/content length.
            string blobSha1;
            string sha256;
            await using (var file = File.OpenRead(staging))
            {
                using var gitHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                gitHash.AppendData(Encoding.ASCII.GetBytes($"blob {info.Length}\0"));
                using var hash256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    gitHash.AppendData(buffer.AsSpan(0, read));
                    hash256.AppendData(buffer.AsSpan(0, read));
                }
                blobSha1 = Convert.ToHexString(gitHash.GetHashAndReset()).ToLowerInvariant();
                sha256 = Convert.ToHexString(hash256.GetHashAndReset()).ToLowerInvariant();
            }
            if (blobSha1 != De430BlobSha1)
                throw new InvalidDataException("Orekit DE430 ephemeris hash does not match the upstream Git blob.");

            File.Move(staging, target, overwrite: true);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "lnxp1990.430.manifest.json"),
                JsonSerializer.Serialize(new { sourceUrl = De430Source, gitBlobSha1 = blobSha1,
                    sha256, bytes = info.Length, downloadedUtc = DateTimeOffset.UtcNow },
                    new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            Log.Information("Orekit DE430 planetary data installed: {File} SHA256={Hash}", target, sha256);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warning(ex, "Orekit DE430 planetary data download failed; solar-system targets need a compatible local ephemeris.");
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }
    private static async Task<bool> IsVerifiedDe430Async(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;
        long length = new FileInfo(path).Length;
        if (length != De430FileSize)
            return false;
        using var gitHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        gitHash.AppendData(Encoding.ASCII.GetBytes($"blob {length}"));
        gitHash.AppendData(new byte[] { 0 });
        await using var file = File.OpenRead(path);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
            gitHash.AppendData(buffer.AsSpan(0, read));
        return Convert.ToHexString(gitHash.GetHashAndReset())
            .Equals(De430BlobSha1, StringComparison.OrdinalIgnoreCase);
    }
}
