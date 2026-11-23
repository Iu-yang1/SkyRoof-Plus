using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

internal sealed partial class HostMediaStatusProbe
{
    public static async Task<HostMediaStatusProbe> StartAsync(
        string hostDll, string mediaPipe, string suffix)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        string url = $"http://127.0.0.1:{port}";

        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        info.ArgumentList.Add(hostDll);
        info.Environment["ASPNETCORE_URLS"] = url;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["SKYROOF_AUTH_DB"] =
            Path.Combine(Path.GetTempPath(), "skyroof-media-host-" + suffix + ".db");
        info.Environment["SKYROOF_BOOTSTRAP_ADMIN_USER"] = "media-test-admin";
        info.Environment["SKYROOF_BOOTSTRAP_ADMIN_PASSWORD"] =
            "ExampleMediaIntegrationP@ssw0rd!";
        info.Environment["SKYROOF_MEDIA_PIPE"] = mediaPipe;
        info.Environment["SKYROOF_STATION_PIPE"] = "missing-station-" + suffix;
        var host = Process.Start(info) ?? throw new InvalidOperationException("Host start failed.");
        var cookies = new HttpClientHandler { CookieContainer = new CookieContainer() };
        var client = new HttpClient(cookies) { BaseAddress = new Uri(url) };
        var probe = new HostMediaStatusProbe(host, client);
        try
        {
            bool live = false;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    var response = await client.GetAsync("/health/live");
                    if (response.IsSuccessStatusCode) { live = true; break; }
                }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            Check(live, "Authenticated Host did not start.");
            using var anonymous = new HttpClient { BaseAddress = new Uri(url) };
            Check((await anonymous.GetAsync("/api/v1/media/rx-status")).StatusCode ==
                HttpStatusCode.Unauthorized, "Unauthenticated RX status was exposed.");
            (await client.PostAsJsonAsync("/api/v1/auth/login",
                new { userName = "media-test-admin",
                    password = "ExampleMediaIntegrationP@ssw0rd!" }))
                .EnsureSuccessStatusCode();
            return probe;
        }
        catch
        {
            await probe.DisposeAsync();
            throw;
        }
    }
}
