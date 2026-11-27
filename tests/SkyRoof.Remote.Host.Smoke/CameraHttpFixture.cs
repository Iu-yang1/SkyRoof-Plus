using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

internal sealed class CameraHttpFixture : IAsyncDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+WWSsAAAAASUVORK5CYII=");
    private readonly WebApplication _app;
    private CameraHttpFixture(WebApplication app) => _app = app;

    internal static async Task<CameraHttpFixture> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapGet("/snapshot", () => Results.File(Png, "image/png"));
        app.MapPost("/onvif", (Delegate)SoapAsync);
        await app.StartAsync();
        return new CameraHttpFixture(app);
    }

    private static async Task<IResult> SoapAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        string request = await reader.ReadToEndAsync(context.RequestAborted);
        string baseUrl = "http://" + context.Request.Host;
        string? body = request.Contains("GetServices", StringComparison.Ordinal) ?
            $"<Service><Namespace>http://www.onvif.org/ver20/media/wsdl</Namespace><XAddr>{baseUrl}/onvif</XAddr></Service><Service><Namespace>http://www.onvif.org/ver20/ptz/wsdl</Namespace><XAddr>{baseUrl}/onvif</XAddr></Service>" :
            request.Contains("GetProfiles", StringComparison.Ordinal) ? "<Profiles token=\"primary\"><Name>Test camera</Name></Profiles>" :
            request.Contains("GetSnapshotUri", StringComparison.Ordinal) ? $"<Uri>{baseUrl}/snapshot</Uri>" :
            request.Contains("GetStreamUri", StringComparison.Ordinal) && Environment.GetEnvironmentVariable("SKYROOF_TEST_RTSP_URI") is { } rtsp ? $"<Uri>{rtsp}</Uri>" :
            ValidatePtz(request);
        return body is null ? Results.BadRequest() : Results.Text(
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>" + body + "</s:Body></s:Envelope>",
            "application/soap+xml");
    }

    private static string? ValidatePtz(string request)
    {
        XNamespace ptz = "http://www.onvif.org/ver20/ptz/wsdl";
        XNamespace tt = "http://www.onvif.org/ver10/schema";
        var doc = XDocument.Parse(request);
        var move = doc.Descendants(ptz + "RelativeMove").SingleOrDefault();
        if (move is not null)
        {
            var vector = move.Element(ptz + "Translation")?.Element(tt + "PanTilt");
            return move.Element(ptz + "ProfileToken")?.Value == "primary" &&
                vector is not null && vector.Attribute("x")?.Value == "-0.25" &&
                vector.Attribute("y")?.Value == "0.125" &&
                vector.Attribute("space")?.Value == "http://www.onvif.org/ver10/tptz/PanTiltSpaces/TranslationGenericSpace" &&
                move.Element(ptz + "Translation")?.Element(tt + "Zoom") is null
                ? "<RelativeMoveResponse/>" : null;
        }
        var stop = doc.Descendants(ptz + "Stop").SingleOrDefault();
        return stop is not null && stop.Element(ptz + "ProfileToken")?.Value == "primary" &&
            stop.Element(ptz + "PanTilt")?.Value == "true" && stop.Element(ptz + "Zoom")?.Value == "true"
            ? "<StopResponse/>" : null;
    }

    internal void Configure(ProcessStartInfo info)
    {
        info.Environment["SKYROOF_ONVIF_ENDPOINT"] = _app.Urls.Single() + "/onvif";
        if (Environment.GetEnvironmentVariable("SKYROOF_TEST_RTSP_URI") is not null)
            info.Environment["SKYROOF_FFMPEG_PATH"] = Environment.GetEnvironmentVariable("SKYROOF_TEST_FFMPEG") ?? "ffmpeg";
    }

    internal static async Task VerifyAsync(HttpClient anonymous, HttpClient authenticated)
    {
        using var denied = await anonymous.GetAsync("/api/v1/camera/profiles");
        if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidDataException("Anonymous camera access.");
        using var profiles = await authenticated.GetAsync("/api/v1/camera/profiles");
        profiles.EnsureSuccessStatusCode();
        string json = await profiles.Content.ReadAsStringAsync();
        if (!json.Contains("primary", StringComparison.Ordinal) || !json.Contains("Test camera", StringComparison.Ordinal))
            throw new InvalidDataException("ONVIF profile missing.");
        using var image = await authenticated.GetAsync("/api/v1/camera/snapshot?profile=primary&media2=true");
        image.EnsureSuccessStatusCode();
        byte[] bytes = await image.Content.ReadAsByteArrayAsync();
        if (!bytes.AsSpan().SequenceEqual(Png) || image.Content.Headers.ContentType?.MediaType != "image/png")
            throw new InvalidDataException("ONVIF snapshot bytes mismatch.");
        var movement = new { profile = "primary", pan = -0.25, tilt = 0.125, zoom = 0 };
        using var anonymousMove = await anonymous.PostAsJsonAsync("/api/v1/camera/ptz/relative", movement);
        if (anonymousMove.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidDataException("Anonymous PTZ was allowed.");
        using var accepted = await authenticated.PostAsJsonAsync("/api/v1/camera/ptz/relative", movement);
        accepted.EnsureSuccessStatusCode();
        using var stopped = await authenticated.PostAsJsonAsync("/api/v1/camera/ptz/stop", new { profile = "primary" });
        stopped.EnsureSuccessStatusCode();
        if (Environment.GetEnvironmentVariable("SKYROOF_TEST_RTSP_URI") is not null)
            await CameraVideoHttpProbe.VerifyAsync(anonymous, authenticated);
        Console.WriteLine("PASS: authenticated Host -> real SOAP GetServices/GetProfiles/GetSnapshotUri -> exact camera image and RelativeMove/Stop wire XML.");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
