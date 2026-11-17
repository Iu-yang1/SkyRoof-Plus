using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Serilog;

namespace SkyRoof;

/// <summary>
/// Ordered source mirrors for China's variable international connectivity.
/// One source is accepted only after its payload validates; lower priority
/// alternatives and the last good disk cache remain independent.
/// </summary>
internal static class OrbitSourceFetcher
{
    internal sealed record DownloadedText(string Content, string Extension, string Source);

    internal static string[] Candidates(string? urls) =>
        SatnogsDb.SplitSourceList(urls);

    internal static string CacheKey(string sources) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sources.Trim())).AsSpan(0, 8))
            .ToLowerInvariant();

    internal static bool LooksLikeSatnogsArray(string content, string requiredKey)
    {
        try
        {
            if (JToken.Parse(content) is not JArray data || data.Count == 0)
                return false;
            return data.OfType<JObject>().Any(item =>
                item.GetValue(requiredKey, StringComparison.OrdinalIgnoreCase) != null);
        }
        catch (Newtonsoft.Json.JsonException) { return false; }
    }

    internal static bool LooksLikeJe9pel(string content) =>
        content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => JE9PELtransmitter.TryParse(line, out _));

    internal static bool LooksLikeSatyamlZip(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.Entries.Any(entry =>
                entry.FullName.Replace('\\', '/')
                    .Contains("/python/satyaml/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException) { return false; }
    }

    internal static async Task<DownloadedText> GetTextAsync(
        HttpClient http, string? sources, int timeoutSeconds,
        Func<string, bool> validate, CancellationToken cancellationToken)
    {
        Exception? last = null;
        var rejectedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string source in Candidates(sources))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri? candidateUrl) &&
                rejectedHosts.Contains(candidateUrl.Host))
                continue;
            try
            {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                request.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
                string content;
                string extension;
                if (Uri.TryCreate(source, UriKind.Absolute, out Uri? url) &&
                    url.Scheme is "http" or "https")
                {
                    content = await http.GetStringAsync(url, request.Token).ConfigureAwait(false);
                    extension = Path.GetExtension(url.AbsolutePath);
                }
                else
                {
                    string path = Environment.ExpandEnvironmentVariables(source);
                    content = await File.ReadAllTextAsync(path, request.Token).ConfigureAwait(false);
                    extension = Path.GetExtension(path);
                }
                if (!validate(content))
                    throw new InvalidDataException("Unrecognized or empty source payload");
                Log.Information("Validated satellite data from {OrbitSource}", source);
                return new DownloadedText(content, extension, source);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                // Do not hammer the same origin after a non-200 HTTP response:
                // the CelesTrak policy explicitly requires stopping requests.
                if (ex is HttpRequestException { StatusCode: not null } &&
                    Uri.TryCreate(source, UriKind.Absolute, out Uri? rejected))
                    rejectedHosts.Add(rejected.Host);
                Log.Warning(ex, "Orbit source {OrbitSource} failed; attempting a different mirror", source);
            }
        }
        throw new InvalidOperationException(
            "No configured satellite data source returned a valid payload", last);
    }

    internal static async Task<byte[]> GetZipAsync(
        HttpClient http, string? sources, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (string source in Candidates(sources))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                request.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
                byte[] bytes;
                if (Uri.TryCreate(source, UriKind.Absolute, out Uri? url) &&
                    url.Scheme is "http" or "https")
                    bytes = await http.GetByteArrayAsync(url, request.Token).ConfigureAwait(false);
                else
                    bytes = await File.ReadAllBytesAsync(
                        Environment.ExpandEnvironmentVariables(source), request.Token).ConfigureAwait(false);
                if (!LooksLikeSatyamlZip(bytes))
                    throw new InvalidDataException("Archive does not contain SatYAML definitions");
                Log.Information("Validated SatYAML archive from {OrbitSource}", source);
                return bytes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                Log.Warning(ex, "SatYAML source {OrbitSource} failed; attempting configured mirror", source);
            }
        }
        throw new InvalidOperationException("No configured SatYAML source returned a valid archive", last);
    }

    internal static void WriteTextAtomically(string destination, string text)
    {
        string tmp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, text);
            File.Move(tmp, destination, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
