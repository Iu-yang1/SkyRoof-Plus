using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SkyRoof.RemoteDesktop;

internal static class StationRuntimeChecks
{
    internal static async Task RunAsync(string pipe, HttpClient anonymous, HttpClient admin, FakeRigctld radio)
    {
        using var client = new StationIpcClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var identity = await client.IdentifyAsync(timeout.Token);
        using var denied = await anonymous.GetAsync("/api/v1/station/runtime", timeout.Token);
        Require(denied.StatusCode == HttpStatusCode.Unauthorized, "Anonymous runtime identity access.");
        using var document = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/station/runtime", timeout.Token));
        Require(document.RootElement.GetProperty("instanceId").GetString() == identity.InstanceId &&
            document.RootElement.GetProperty("stationId").GetString() == identity.StationId, "Host resolves a different Station.");
        int pttBefore = radio.Count("T 1");
        foreach (string mode in new[] { "ft4", "q65" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dsp/" + mode + "/auto")
            {
                Content = JsonContent.Create(new { call = "K1ABC", grid = "FN42", frequencyHz = 145900000 })
            };
            request.Headers.Add("X-SkyRoof-Station-Instance", Guid.NewGuid().ToString("N"));
            using var reply = await admin.SendAsync(request, timeout.Token);
            Require(reply.StatusCode == HttpStatusCode.Conflict, "Auto QSO accepted another Station instance.");
        }
        Require(radio.Count("T 1") == pttBefore, "Mismatched Station request keyed PTT.");
        Console.WriteLine("PASS: Host and native IPC identify same Station; wrong-instance FT4/Q65 auto requests rejected before PTT.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
