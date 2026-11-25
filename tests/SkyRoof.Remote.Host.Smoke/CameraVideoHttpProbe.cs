using System.Net;
using System.Text;

internal static class CameraVideoHttpProbe
{
    internal static async Task VerifyAsync(HttpClient anonymous, HttpClient authenticated)
    {
        const string route = "/api/v1/camera/video?profile=primary&media2=true";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var denied = await anonymous.GetAsync(route, timeout.Token);
        if (denied.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidDataException("Anonymous RTSP relay allowed.");
        using var response = await authenticated.GetAsync(route, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "video/mp4")
            throw new InvalidDataException("RTSP relay content type mismatch.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var prefix = new MemoryStream();
        byte[] chunk = new byte[16384];
        while (prefix.Length < 1024 * 1024)
        {
            int count = await input.ReadAsync(chunk, timeout.Token);
            if (count == 0) break;
            prefix.Write(chunk, 0, count);
            string boxes = Encoding.Latin1.GetString(prefix.ToArray());
            if (boxes.Contains("ftyp", StringComparison.Ordinal) && boxes.Contains("avcC", StringComparison.Ordinal) &&
                boxes.Contains("moof", StringComparison.Ordinal) && boxes.Contains("mdat", StringComparison.Ordinal))
            {
                Console.WriteLine("PASS: authenticated live ONVIF SOAP GetStreamUri -> real RTSP/TCP RTP H.264 -> FFmpeg copy remux -> fragmented MP4.");
                return;
            }
        }
        throw new InvalidDataException("Camera stream did not deliver H.264 fragmented MP4.");
    }
}
