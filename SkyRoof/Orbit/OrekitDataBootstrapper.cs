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
    private const string De430BlobSha1 = "70ec728d3d5018ff3df2eaa4ae439e668de7ef6b";
    private const long De430FileSize = 7_459_904;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);
    private sealed record VerifiedFile(string Path, long Length, DateTime LastWriteUtc);
    private static VerifiedFile? verifiedDe430;

    internal static async Task EnsureAsync(OrbitSourceSettings settings, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!settings.AutoDownloadOrekitData && !forceRefresh)
            return;

        // Automatic startup refresh and the manual Settings action share the
        // same staging filenames. Serialize them without blocking WinForms.
        await DownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureDataAsync(settings, forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    private static async Task EnsureDataAsync(OrbitSourceSettings settings, bool forceRefresh, CancellationToken cancellationToken)
    {
        string directory = OrbitBackendRegistry.ResolveDataDirectory(settings.OrekitDataDirectory);
        Directory.CreateDirectory(directory);

        string eop = Path.Combine(directory, "finals2000A.all");
        if (forceRefresh || !File.Exists(eop) ||
            DateTime.UtcNow - File.GetLastWriteTimeUtc(eop) > TimeSpan.FromDays(7))
        {
            try
            {
                var result = await OrbitEopDataUpdater.DownloadAsync(settings, cancellationToken).ConfigureAwait(false);
                Log.Information("Orekit IERS EOP downloaded: {File} SHA256={Hash}",
                    result.LocalFile, result.Sha256);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning(ex, "IERS EOP refresh failed; preserving the local dataset.");
            }
        }

        string target = Path.Combine(directory, "lnxp1990.430");
        if (await IsVerifiedDe430Async(target, cancellationToken).ConfigureAwait(false))
            return;

        string staging = target + ".download";
        try
        {
            using var response = await Client.GetAsync(
                settings.OrekitDe430Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);

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
                while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
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
            verifiedDe430 = new VerifiedFile(target, info.Length, File.GetLastWriteTimeUtc(target));
            await File.WriteAllTextAsync(
                Path.Combine(directory, "lnxp1990.430.manifest.json"),
                JsonSerializer.Serialize(new { sourceUrl = settings.OrekitDe430Url, gitBlobSha1 = blobSha1,
                    sha256, bytes = info.Length, downloadedUtc = DateTimeOffset.UtcNow },
                    new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
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
        var info = new FileInfo(path);
        long length = info.Length;
        if (length != De430FileSize)
            return false;
        var last = verifiedDe430;
        if (last != null && string.Equals(last.Path, path, StringComparison.OrdinalIgnoreCase) &&
            last.Length == length && last.LastWriteUtc == info.LastWriteTimeUtc)
            return true;
        using var gitHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        gitHash.AppendData(Encoding.ASCII.GetBytes($"blob {length}"));
        gitHash.AppendData(new byte[] { 0 });
        await using var file = File.OpenRead(path);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
            gitHash.AppendData(buffer.AsSpan(0, read));
        bool valid = Convert.ToHexString(gitHash.GetHashAndReset())
            .Equals(De430BlobSha1, StringComparison.OrdinalIgnoreCase);
        if (valid)
            verifiedDe430 = new VerifiedFile(path, length, info.LastWriteTimeUtc);
        return valid;
    }
}
