using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

internal static class SdrHttpChecks
{
    internal static async Task RunAsync(HttpClient anonymous, HttpClient admin)
    {
        using var response = await anonymous.GetAsync("/api/v1/sdr/");
        Require(response.StatusCode == HttpStatusCode.Unauthorized, "Anonymous SDR status was authorized");
        using var status = await admin.GetAsync("/api/v1/sdr/");
        status.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Require(!json.RootElement.GetProperty("configured").GetBoolean(), "Unconfigured station SDR unexpectedly available");
        using var invalid = await admin.PutAsJsonAsync("/api/v1/sdr/", new { centerHz = 435000000, channelHz = 450000000, sampleRate = 1000000 });
        Require(invalid.StatusCode == HttpStatusCode.BadRequest, "Out-of-band SDR configuration was not rejected");
        using var unavailable = await admin.PutAsJsonAsync("/api/v1/sdr/", new { centerHz = 435000000, channelHz = 435000000, sampleRate = 1000000 });
        Require(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable, "Unconfigured SDR launched unexpectedly");
        using var stopped = await admin.DeleteAsync("/api/v1/sdr/");
        stopped.EnsureSuccessStatusCode();
        Console.WriteLine("PASS: real Host/Station SDR authorization, request validation and unconfigured lifecycle.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
