using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

internal static class CwHttpChecks
{
    public static async Task RunAsync(HttpClient anonymous, HttpClient admin, FakeRigctld radio)
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var denied = await anonymous.PostAsJsonAsync("/api/v1/device/cw/send", new { text = "CQ" }, ct.Token);
        if (denied.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidDataException("Anonymous CW send was authorized.");
        using var speed = await admin.PutAsJsonAsync("/api/v1/device/cw/speed", new { wpm = 25 }, ct.Token);
        speed.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await speed.Content.ReadAsStringAsync(ct.Token));
        if (json.RootElement.GetProperty("keySpeedRaw").GetInt32() != 115)
            throw new InvalidDataException("Host CW WPM readback mismatch.");
        using var send = await admin.PostAsJsonAsync("/api/v1/device/cw/send", new { text = "CQ" }, ct.Token);
        if (send.StatusCode != HttpStatusCode.Conflict || radio.Count("SEND CQ") != 0)
            throw new InvalidDataException("CW sent while simulated TX mode is USB.");
        Console.WriteLine("PASS: authenticated Host -> Station -> Device CW WPM readback, anonymous denial and live TX-mode rejection.");
    }
}
