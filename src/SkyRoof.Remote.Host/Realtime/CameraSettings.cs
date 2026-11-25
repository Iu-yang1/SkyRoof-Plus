using System.Net;

namespace SkyRoof.Remote.Host.Realtime;

internal sealed record CameraSettings(string Endpoint, string Username, string Password)
{
    internal static CameraSettings Read() => new(
        Environment.GetEnvironmentVariable("SKYROOF_ONVIF_ENDPOINT") ?? "",
        Environment.GetEnvironmentVariable("SKYROOF_ONVIF_USERNAME") ?? "",
        Environment.GetEnvironmentVariable("SKYROOF_ONVIF_PASSWORD") ?? "");

    internal SkyRoof.OnvifProtocolClient Open() => new(Endpoint, Username, Password, sameHostOnly: true);

    internal static bool IsProtocolError(Exception error) =>
        error is ArgumentException or HttpRequestException or InvalidOperationException or IOException or
            System.Xml.XmlException or OperationCanceledException;

    internal async Task<(byte[] Bytes, string ContentType)> ReadSnapshotAsync(Uri uri, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false,
            Credentials = Username.Length == 0 ? null : new NetworkCredential(Username, Password) };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        string? type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("image/jpeg" or "image/png")) throw new InvalidDataException("Camera snapshot is not JPEG/PNG.");
        const int maximum = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Camera snapshot exceeds size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var result = new MemoryStream();
        byte[] buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (result.Length + count > maximum) throw new InvalidDataException("Camera snapshot exceeds size limit.");
            result.Write(buffer, 0, count);
        }
        return (result.ToArray(), type);
    }
}
