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
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
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

    private static async Task EnsureDataAsync(
        OrbitSourceSettings settings, bool forceRefresh,
        CancellationToken cancellationToken)
    {
        string directory = OrbitBackendRegistry.ResolveDataDirectory(settings.OrekitDataDirectory);
        Directory.CreateDirectory(directory);
        await RefreshEopAsync(settings, directory, forceRefresh, cancellationToken)
            .ConfigureAwait(false);
        await EnsureDe430Async(settings, directory, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RefreshEopAsync(
        OrbitSourceSettings settings, string directory, bool forceRefresh,
        CancellationToken cancellationToken)
    {
        string eop = Path.Combine(directory, "finals2000A.all");
        if (!forceRefresh && File.Exists(eop) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(eop) <= TimeSpan.FromDays(7))
            return;
        try
        {
            var result = await OrbitEopDataUpdater.DownloadAsync(settings, cancellationToken)
                .ConfigureAwait(false);
            Log.Information("Orekit IERS EOP downloaded: {File} SHA256={Hash}",
                result.LocalFile, result.Sha256);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warning(ex, "IERS EOP refresh failed; preserving the local dataset.");
        }
    }

    private static async Task EnsureDe430Async(
        OrbitSourceSettings settings, string directory, CancellationToken cancellationToken)
    {
        string target = Path.Combine(directory, "lnxp1990.430");
        if (await IsVerifiedDe430Async(target, cancellationToken).ConfigureAwait(false))
            return;

        string staging = target + ".download";
        foreach (string source in SatnogsDb.SplitSourceList(settings.OrekitDe430Url))
        {
            try
            {
                await StageDe430Async(source, staging, cancellationToken).ConfigureAwait(false);
                string sha256 = await VerifyDe430Async(staging, cancellationToken)
                    .ConfigureAwait(false);

                File.Move(staging, target, overwrite: true);
                var info = new FileInfo(target);
                verifiedDe430 = new VerifiedFile(target, info.Length, info.LastWriteTimeUtc);
                await WriteManifestAsync(directory, source, sha256, info.Length, cancellationToken)
                    .ConfigureAwait(false);
                Log.Information("Orekit DE430 installed from {Source}: SHA256={Hash}", source, sha256);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning(ex, "Orekit DE430 mirror {Source} failed validation; trying next", source);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }
        Log.Warning("No DE430 mirror yielded a verified pinned ephemeris; keeping existing cache.");
    }

    private static async Task StageDe430Async(
        string source, string destination, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? url) &&
            url.Scheme is "http" or "https")
        {
            using var response = await Client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        else
            File.Copy(Environment.ExpandEnvironmentVariables(source), destination, overwrite: true);
    }

    private static async Task<string> VerifyDe430Async(
        string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != De430FileSize)
            throw new InvalidDataException("Orekit DE430 data length does not match the pinned release.");

        using var gitHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        gitHash.AppendData(Encoding.ASCII.GetBytes($"blob {info.Length}\0"));
        using var hash256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var file = File.OpenRead(path);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            gitHash.AppendData(buffer.AsSpan(0, read));
            hash256.AppendData(buffer.AsSpan(0, read));
        }
        string blobSha1 = Convert.ToHexString(gitHash.GetHashAndReset()).ToLowerInvariant();
        if (blobSha1 != De430BlobSha1)
            throw new InvalidDataException("DE430 hash does not match the pinned upstream Git blob.");
        return Convert.ToHexString(hash256.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task WriteManifestAsync(
        string directory, string source, string sha256, long bytes,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(directory, "lnxp1990.430.manifest.json"),
            JsonSerializer.Serialize(new
            {
                sourceUrl = source,
                gitBlobSha1 = De430BlobSha1,
                sha256,
                bytes,
                downloadedUtc = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
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
