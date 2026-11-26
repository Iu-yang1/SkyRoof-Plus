using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SkyRoof.Remote.Host.Realtime;
using SkyRoof.Remote.Ipc;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
await EopChecks.RunAsync();
if (args.Length != 3) throw new ArgumentException("Device.dll Station.dll Host.dll required");
var deviceDll = Path.GetFullPath(args[0]);
var stationDll = Path.GetFullPath(args[1]);
var hostDll = Path.GetFullPath(args[2]);
var suffix = Guid.NewGuid().ToString("N");
var devicePipe = "skyroof-e2e-device-" + suffix;
var stationPipe = "skyroof-e2e-station-" + suffix;
var journal = Path.Combine(Path.GetTempPath(), "skyroof-e2e-ptt-" + suffix + ".json");
var db = Path.Combine(Path.GetTempPath(), "skyroof-e2e-db-" + suffix + ".db");
var trackingFile = Path.Combine(Path.GetTempPath(), "skyroof-e2e-tracking-" + suffix + ".json");
var catalogFile = Path.Combine(Path.GetTempPath(), "skyroof-e2e-catalog-" + suffix + ".json");
CatalogChecks.WriteFixture(catalogFile);
await CatalogOnlineFixture.WriteSnapshotsAsync(catalogFile);

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var baseUrl = $"http://127.0.0.1:{port}";

await using var radio = new FakeRigctld();
await using var rotator = new FakeRigctld();
ProcessStartInfo Setup(string dll)
{
    var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    info.ArgumentList.Add(dll);
    return info;
}
var deviceInfo = Setup(deviceDll);
deviceInfo.Environment["SKYROOF_DEVICE_PIPE"] = devicePipe;
deviceInfo.Environment["SKYROOF_CAT_HOST"] = "127.0.0.1";
deviceInfo.Environment["SKYROOF_CAT_PORT"] = radio.Port.ToString();
deviceInfo.Environment["SKYROOF_CAT_DIALECT"] = "skycat";
deviceInfo.Environment["SKYROOF_CW_KEYER_PORT"] = radio.Port.ToString();
deviceInfo.Environment["SKYROOF_DEVICE_PTT_JOURNAL"] = journal;
deviceInfo.Environment["SKYROOF_ROTATOR_HOST"] = "127.0.0.1";
deviceInfo.Environment["SKYROOF_ROTATOR_PORT"] = rotator.Port.ToString();
var signalBus = "skyroof-desktop-signal-" + suffix;
var stationInfo = Setup(stationDll);
stationInfo.Environment["SKYROOF_RX_AUDIO_BUS"] = signalBus;
stationInfo.Environment.Remove("SKYROOF_SDR_BRIDGE_PATH");
stationInfo.Environment.Remove("SKYROOF_SDR_DEVICE_ARGS");
stationInfo.Environment["SKYROOF_RX_AUDIO_CHANNELS"] = "1";
stationInfo.Environment["SKYROOF_DEVICE_PIPE"] = devicePipe;
stationInfo.Environment["SKYROOF_STATION_PIPE"] = stationPipe;
stationInfo.Environment["SKYROOF_TRACKING_JOB_FILE"] = trackingFile;
stationInfo.Environment["SKYROOF_LOGBOOK_DIRECTORY"] = trackingFile + "-logbook";
stationInfo.Environment["SKYROOF_SATELLITE_CATALOG"] = catalogFile;
stationInfo.Environment["SKYROOF_ORBIT_BACKEND"] = Environment.GetEnvironmentVariable("SKYROOF_TEST_ORBIT_BACKEND") ?? "orekit";
stationInfo.Environment["SKYROOF_OREKIT_DATA"] = Path.Combine(Path.GetDirectoryName(stationDll)!, "orekit-data");
stationInfo.Environment["SKYROOF_OREKIT_EOP_AUTO"] = "false";
stationInfo.Environment["SKYROOF_ISS_OEM_AUTO"] = "false";
stationInfo.Environment["SKYROOF_CATALOG_AUTO"] = "false";
var hostInfo = Setup(hostDll);
hostInfo.Environment["SKYROOF_STATION_PIPE"] = stationPipe;
hostInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
hostInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
hostInfo.Environment["SKYROOF_AUTH_DB"] = db;
hostInfo.Environment["SKYROOF_BOOTSTRAP_ADMIN_USER"] = "remote-admin";
hostInfo.Environment["SKYROOF_BOOTSTRAP_ADMIN_PASSWORD"] = "SimulatedStationP@ssw0rd2026";

using var device = Process.Start(deviceInfo) ?? throw new InvalidOperationException("Device start failed");
using var station = Process.Start(stationInfo) ?? throw new InvalidOperationException("Station start failed");
using var host = Process.Start(hostInfo) ?? throw new InvalidOperationException("Host start failed");
Process? restartedStation = null;
try
{
    using var anonymous = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var alive = false;
    for (var i = 0; i < 120; i++)
    {
        try
        {
            if ((await anonymous.GetAsync("/health/ready")).IsSuccessStatusCode)
            {
                alive = true;
                break;
            }
        }
        catch (HttpRequestException) { }
        await Task.Delay(150);
    }
    Ensure(alive, "Host never became ready");
    await DesktopIpcChecks.RunAsync(stationPipe, radio, rotator);
    await LogbookChecks.RunAsync(stationPipe);
    await DesktopSignalChecks.RunAsync(stationPipe, signalBus);
    Ensure((await anonymous.GetAsync("/api/v1/station")).StatusCode ==
        HttpStatusCode.Unauthorized, "Anonymous station access allowed");

    using var cookieHandler = new HttpClientHandler { CookieContainer = new CookieContainer() };
    using var admin = new HttpClient(cookieHandler) { BaseAddress = new Uri(baseUrl) };
    (await admin.PostAsJsonAsync("/api/v1/auth/login",
        new { userName = "remote-admin", password = "SimulatedStationP@ssw0rd2026" }))
        .EnsureSuccessStatusCode();
    await SdrHttpChecks.RunAsync(anonymous, admin);
    await CatalogChecks.RunAsync(anonymous, admin);
    await CatalogOnlineFixture.VerifyStationAsync(admin);
    var tokenReply = await admin.PostAsJsonAsync("/api/v1/auth/tokens",
        new { name = "end-to-end" });
    tokenReply.EnsureSuccessStatusCode();
    using var tokenDoc = JsonDocument.Parse(await tokenReply.Content.ReadAsStringAsync());
    var token = tokenDoc.RootElement.GetProperty("token").GetString()!;
    var tokenId = tokenDoc.RootElement.GetProperty("id").GetGuid();
    await StationRuntimeChecks.RunAsync(stationPipe, anonymous, admin, radio);
    var added = await admin.PostAsJsonAsync("/api/v1/admin/users", new {
        userName = "field-operator", password = "SimulationOperatorP@ssw0rd!", role = "Operator"
    });
    Ensure(added.StatusCode == HttpStatusCode.Created, "Admin could not provision separate Operator");
    using var createdUser = JsonDocument.Parse(await added.Content.ReadAsStringAsync());
    var operatorId = createdUser.RootElement.GetProperty("id").GetString()!;
    using var operatorCookies = new HttpClientHandler { CookieContainer = new CookieContainer() };
    using var operatorClient = new HttpClient(operatorCookies) { BaseAddress = new Uri(baseUrl) };
    (await operatorClient.PostAsJsonAsync("/api/v1/auth/login", new {
        userName = "field-operator", password = "SimulationOperatorP@ssw0rd!"
    })).EnsureSuccessStatusCode();
    var operatorTokenResult = await operatorClient.PostAsJsonAsync("/api/v1/auth/tokens",
        new { name = "operator-client" });
    operatorTokenResult.EnsureSuccessStatusCode();
    using var operatorTokenDoc = JsonDocument.Parse(await operatorTokenResult.Content.ReadAsStringAsync());
    var operatorToken = operatorTokenDoc.RootElement.GetProperty("token").GetString()!;

    HubConnection CreateHub(string credential) => new HubConnectionBuilder()
        .WithUrl(baseUrl + "/hub/control", options =>
            options.AccessTokenProvider = () => Task.FromResult<string?>(credential))
        .AddMessagePackProtocol().Build();


    await CwHttpChecks.RunAsync(anonymous, admin, radio);
    await using var a = CreateHub(token);
    await using var b = CreateHub(operatorToken);
    await a.StartAsync();
    await b.StartAsync();
    var initial = await CatStartupChecks.WaitReadbackAsync(a);
    Ensure(initial.RxFrequencyHz == 145_900_000 && !initial.ActualPtt,
        "Host -> Station -> Device readback was not real");

    var first = await a.InvokeAsync<RemoteCatStatus>("ExecuteCatCommand",
        new RemoteCatCommand("A-ptt", DeviceOperation.DeviceManualBegin,
            435_650_000, "USB", "LAN"));
    Ensure(first.ActualPtt && radio.Count("T 1") == 1,
        "Authenticated Host did not assert CAT PTT");
    var takeover = await b.InvokeAsync<RemoteCatStatus>("ExecuteCatCommand",
        new RemoteCatCommand("B-ptt", DeviceOperation.DeviceManualBegin,
            435_650_000, "USB", "LAN"));
    Ensure(takeover.ActualPtt && takeover.Outcome == "source-takeover-no-rf-transition"
        && radio.Count("T 1") == 1 && radio.Count("T 0") == 0,
        "Second SignalR operator unexpectedly toggled RF");
    await a.StopAsync();
    var afterOldDisconnect = await b.InvokeAsync<RemoteCatStatus>("GetCatReadback");
    Ensure(afterOldDisconnect.ActualPtt && radio.Count("T 0") == 0,
        "Disconnecting the superseded Operator released another user's PTT");
    await b.StopAsync();
    var off = false;
    for (var i = 0; i < 40; i++)
    {
        if (radio.Count("T 0") == 1 && !File.Exists(journal)) { off = true; break; }
        await Task.Delay(100);
    }
    Ensure(off && !File.Exists(journal),
        "Releasing active SignalR session did not clear PTT and its lease");

    await using (var tracker = CreateHub(operatorToken))
    {
        await tracker.StartAsync();
        await TrackingChecks.RunAsync(tracker, radio, rotator);
        await MoonTrackingChecks.RunAsync(tracker, stationDll, radio);
        await OmmFixture.RunAsync(tracker);
        await RotatorChecks.RunAsync(tracker, rotator);
        await tracker.StopAsync();
    }

    restartedStation = await ContinuousTrackingChecks.RunAsync(
        () => CreateHub(operatorToken), station, stationInfo, radio, rotator, trackingFile);

    await using var revoked = CreateHub(token);
    await revoked.StartAsync();
    (await admin.DeleteAsync("/api/v1/auth/tokens/" + tokenId)).EnsureSuccessStatusCode();
    var denied = false;
    try { await revoked.InvokeAsync<RemoteCatStatus>("GetCatReadback"); }
    catch (Microsoft.AspNetCore.SignalR.HubException) { denied = true; }
    Ensure(denied, "Previously authenticated SignalR token remained authorized after revocation");
    await revoked.StopAsync();

    var updatedRole = await admin.PutAsJsonAsync("/api/v1/admin/users/" + operatorId + "/role",
        new { role = "Viewer" });
    updatedRole.EnsureSuccessStatusCode();
    await using var viewer = CreateHub(operatorToken);
    await viewer.StartAsync();
    var viewerRadio = await CatStartupChecks.WaitReadbackAsync(viewer);
    Ensure(!viewerRadio.ActualPtt, "Viewer did not receive authorized radio status");
    var viewerPrediction = await viewer.InvokeAsync<RemoteTrackingStatus>("PredictTracking", TrackingChecks.Request());
    Ensure(viewerPrediction.NoradNumber == 5 && !viewerPrediction.CatApplied,
        "Viewer cannot access read-only orbital predictions");
    var viewerPosition = await viewer.InvokeAsync<RemoteRotatorPosition>("GetRotatorPosition");
    Ensure(double.IsFinite(viewerPosition.AzimuthDegrees), "Viewer cannot read rotator feedback");
    var viewerRotatorDenied = false;
    try { await viewer.InvokeAsync<RemoteRotatorPosition>("PointRotator", 90, 15); }
    catch (Microsoft.AspNetCore.SignalR.HubException) { viewerRotatorDenied = true; }
    Ensure(viewerRotatorDenied, "Viewer was allowed to control the rotator");
    var trackingDenied = false;
    try
    {
        await viewer.InvokeAsync<RemoteTrackingStatus>("ApplyTracking", TrackingChecks.Request());
    }
    catch (Microsoft.AspNetCore.SignalR.HubException) { trackingDenied = true; }
    Ensure(trackingDenied, "Viewer could apply orbital tracking");
    var viewerDenied = false;
    try {
        await viewer.InvokeAsync<RemoteCatStatus>("ExecuteCatCommand",
            new RemoteCatCommand("viewer-ptt", DeviceOperation.DeviceManualBegin));
    }
    catch (Microsoft.AspNetCore.SignalR.HubException) { viewerDenied = true; }
    Ensure(viewerDenied && radio.Count("T 1") == 1,
        "Viewer role was allowed to transmit through a stale token");
    await viewer.StopAsync();
    await DesktopPttChecks.RunAsync(stationPipe, radio);
    await DesktopCwChecks.RunAsync(stationPipe, radio);
    Console.WriteLine("PASS: authenticated Host -> Station Core -> real CAT Device; two operators, disconnect and token revocation.");
}
finally
{
    if (restartedStation is not null)
    {
        if (!restartedStation.HasExited) restartedStation.Kill(entireProcessTree: true);
        await restartedStation.WaitForExitAsync();
        restartedStation.Dispose();
    }
    foreach (var process in new[] { host, station, device })
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
    await Task.WhenAll(host.WaitForExitAsync(), station.WaitForExitAsync(), device.WaitForExitAsync());
    if (File.Exists(journal)) File.Delete(journal);
    if (File.Exists(trackingFile)) File.Delete(trackingFile);
    File.Delete(catalogFile);
    File.Delete(catalogFile + ".satnogs.json");
    File.Delete(catalogFile + ".celestrak.json");
}
static void Ensure(bool valid, string message)
{
    if (!valid) throw new InvalidOperationException(message);
}
