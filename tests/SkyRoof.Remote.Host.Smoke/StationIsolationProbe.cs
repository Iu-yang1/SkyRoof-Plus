using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

internal static class StationIsolationProbe
{
    private sealed record Instance(string Id, string Url, string Directory, string Password);

    internal static async Task VerifyAsync(string dll)
    {
        var root = Path.Combine(Path.GetTempPath(), "skyroof-station-isolation-" + Guid.NewGuid().ToString("N"));
        var a = new Instance("alpha", ReserveUrl(), Path.Combine(root, "alpha"), "FixtureAlphaPassw0rd!");
        var b = new Instance("beta", ReserveUrl(), Path.Combine(root, "beta"), "FixtureBetaPassw0rd!");
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        using var browser = new HttpClient(handler);
        using var bare = new HttpClient(new HttpClientHandler { UseCookies = false });
        Process? first = null;
        Process? second = null;
        try
        {
            first = await StartAsync(dll, a);
            second = await StartAsync(dll, b);
            await LoginAsync(browser, a);
            await AssertStationAsync(browser, a);
            await RequireUnauthorizedAsync(browser, b);
            var aToken = await TokenAsync(browser, a);
            await LoginAsync(browser, b);
            await AssertStationAsync(browser, b);
            await AssertStationAsync(browser, a);
            var bToken = await TokenAsync(browser, b);
            await VerifyBearerAsync(bare, a, aToken, HttpStatusCode.OK);
            await VerifyBearerAsync(bare, b, aToken, HttpStatusCode.Unauthorized);
            await VerifyBearerAsync(bare, b, bToken, HttpStatusCode.OK);
            await VerifyBearerAsync(bare, a, bToken, HttpStatusCode.Unauthorized);

            await StopAsync(first);
            first = null;
            await StationSchemaProbe.PrepareLegacyAsync(a.Directory);
            first = await StartAsync(dll, a);
            await StationSchemaProbe.VerifyMigratedAsync(a.Directory);
            await AssertStationAsync(browser, a);
            await AssertStationAsync(browser, b);
            await VerifyBearerAsync(bare, a, aToken, HttpStatusCode.OK);
            Console.WriteLine("PASS: two actual Hosts isolate cookies, tokens and SQLite; cookie/key ring survives process restart.");
        }
        finally
        {
            await StopAsync(first);
            await StopAsync(second);
            await DeleteFixtureAsync(root);
        }
    }

    private static async Task DeleteFixtureAsync(string root)
    {
        for (int attempt = 0; Directory.Exists(root); attempt++)
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException error) when (attempt < 20 && (error.HResult & 0xffff) is 32 or 33)
            { await Task.Delay(100); }
        }
    }

    private static string ReserveUrl()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
    }

    private static async Task<Process> StartAsync(string dll, Instance instance)
    {
        Directory.CreateDirectory(instance.Directory);
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        info.ArgumentList.Add(Path.GetFullPath(dll));
        info.Environment["ASPNETCORE_URLS"] = instance.Url;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["SKYROOF_STATION_ID"] = instance.Id;
        info.Environment["SKYROOF_AUTH_DB"] = Path.Combine(instance.Directory, "auth.db");
        info.Environment["SKYROOF_AUTH_KEYS_DIRECTORY"] = Path.Combine(instance.Directory, "keys");
        info.Environment["SKYROOF_BOOTSTRAP_ADMIN_USER"] = "station-admin";
        info.Environment["SKYROOF_BOOTSTRAP_ADMIN_PASSWORD"] = instance.Password;
        var process = Process.Start(info) ?? throw new InvalidOperationException("Host startup failed.");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!deadline.IsCancellationRequested && !process.HasExited)
            {
                try
                {
                    using var response = await http.GetAsync(instance.Url + "/health/live", deadline.Token);
                    if (response.IsSuccessStatusCode) return process;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(150, deadline.Token);
            }
            throw new InvalidOperationException("Station did not become live: " + instance.Id);
        }
        catch { await StopAsync(process); throw; }
    }

    private static async Task StopAsync(Process? process)
    {
        if (process is null) return;
        using (process)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static async Task LoginAsync(HttpClient http, Instance instance)
    {
        using var response = await http.PostAsJsonAsync(instance.Url + "/api/v1/auth/login",
            new { userName = "station-admin", password = instance.Password });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> TokenAsync(HttpClient http, Instance instance)
    {
        using var response = await http.PostAsJsonAsync(instance.Url + "/api/v1/auth/tokens", new { name = "Isolation fixture" });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString() ?? throw new InvalidDataException("Token missing.");
    }

    private static async Task AssertStationAsync(HttpClient http, Instance instance)
    {
        using var response = await http.GetAsync(instance.Url + "/api/v1/station");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (body.RootElement.GetProperty("stationId").GetString() != instance.Id)
            throw new InvalidDataException("Incorrect station identity.");
    }

    private static async Task RequireUnauthorizedAsync(HttpClient http, Instance instance)
    {
        using var response = await http.GetAsync(instance.Url + "/api/v1/station");
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Cookie crossed station boundary.");
    }

    private static async Task VerifyBearerAsync(HttpClient http, Instance instance, string token, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, instance.Url + "/api/v1/station");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        if (response.StatusCode != expected)
            throw new InvalidOperationException("Unexpected station bearer authorization.");
    }
}
