using System.Net;
using System.Diagnostics;
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
            $"<Service><Namespace>http://www.onvif.org/ver20/media/wsdl</Namespace><XAddr>{baseUrl}/onvif</XAddr></Service>" :
            request.Contains("GetProfiles", StringComparison.Ordinal) ? "<Profiles token=\"primary\"><Name>Test camera</Name></Profiles>" :
            request.Contains("GetSnapshotUri", StringComparison.Ordinal) ? $"<Uri>{baseUrl}/snapshot</Uri>" : null;
        return body is null ? Results.BadRequest() : Results.Text(
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>" + body + "</s:Body></s:Envelope>",
            "application/soap+xml");
    }

    internal void Configure(ProcessStartInfo info) =>
        info.Environment["SKYROOF_ONVIF_ENDPOINT"] = _app.Urls.Single() + "/onvif";

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
        Console.WriteLine("PASS: authenticated Host -> real SOAP GetServices/GetProfiles/GetSnapshotUri -> exact camera image.");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
