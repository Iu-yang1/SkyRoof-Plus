using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SkyRoof.RemoteDesktop;

internal sealed class DesktopAutoSession : IDisposable
{
    private readonly HttpClient http;
    private HttpResponseMessage? response;
    private StreamReader? reader;
    private bool terminal;

    internal DesktopAutoSession(Uri station, string token)
    {
        if (!station.IsAbsoluteUri || !string.IsNullOrEmpty(station.UserInfo) ||
            station.Scheme != Uri.UriSchemeHttps && !(station.Scheme == Uri.UriSchemeHttp && station.IsLoopback))
            throw new ArgumentException("Use an HTTPS station origin or loopback HTTP.");
        http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, MaxResponseDrainSize = 0 })
        {
            BaseAddress = new Uri(station.GetLeftPart(UriPartial.Authority)),
            Timeout = Timeout.InfiniteTimeSpan
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    internal async Task<string> StationAsync(CancellationToken token)
    {
        using var reply = await http.GetAsync("/api/v1/station", token);
        reply.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync(token));
        return doc.RootElement.GetProperty("stationId").GetString() ?? "";
    }

    internal async Task StartAsync(string mode, object request, CancellationToken token)
    {
        if (response is not null) throw new InvalidOperationException("Automatic session already opened.");
        terminal = false;
        string route = mode switch
        {
            "FT4" => "/api/v1/dsp/ft4/auto",
            "Q65" => "/api/v1/dsp/q65/auto",
            _ => throw new ArgumentException("Unknown automatic digital mode.")
        };
        using var post = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(request) };
        response = await http.SendAsync(post, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Auto QSO HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
        reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
    }

    internal async Task<JsonElement?> ReadAsync(CancellationToken token)
    {
        var input = reader ?? throw new InvalidOperationException("Automatic session has not started.");
        while (await input.ReadLineAsync(token) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json = JsonDocument.Parse(line);
            var evt = json.RootElement.Clone();
            var state = evt.GetProperty("state").GetString();
            if (terminal) throw new InvalidDataException("Event arrived after terminal automatic QSO receipt.");
            terminal = state is "completed" or "attempts-exhausted" or "error";
            return evt;
        }
        if (!terminal) throw new EndOfStreamException("Automatic QSO ended without a terminal receipt; check CAT RX.");
        return null;
    }

    internal void End()
    {
        reader?.Dispose();
        response?.Dispose();
        reader = null;
        response = null;
        terminal = false;
    }

    public void Dispose()
    {
        End();
        http.Dispose();
    }
}
