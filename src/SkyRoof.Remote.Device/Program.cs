using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using SkyRoof.Remote.Device;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Named Pipe Device runtime.");
var pipe = Environment.GetEnvironmentVariable("SKYROOF_DEVICE_PIPE") ?? "skyroof-station-device";
var host = Environment.GetEnvironmentVariable("SKYROOF_CAT_HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_CAT_PORT"), out var selected)
    ? selected : 4532;
var dialect = Environment.GetEnvironmentVariable("SKYROOF_CAT_DIALECT")?.ToLowerInvariant() switch
{
    "rigctld" => CatDialect.Rigctld,
    "icom-lan-satellite" => CatDialect.IcomLanSatellite,
    _ => CatDialect.SkyCat
};
var nativeHost = Environment.GetEnvironmentVariable("SKYROOF_ICOM_LAN_HOST");
bool nativeCat = dialect == CatDialect.IcomLanSatellite;
if (nativeCat && string.IsNullOrWhiteSpace(nativeHost))
    throw new InvalidOperationException("Native CAT requires SKYROOF_ICOM_LAN_HOST.");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
    server.ListenNamedPipe(pipe, options => options.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc();
builder.Services.AddSingleton(new RigctldLink(host, port));
var journal = Environment.GetEnvironmentVariable("SKYROOF_DEVICE_PTT_JOURNAL");
if (string.IsNullOrWhiteSpace(journal))
{
    var stationId = Environment.GetEnvironmentVariable("SKYROOF_STATION_ID") ?? "default";
    var stableId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(stationId)))[..16];
    journal = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData),
        "SkyRoof", "Remote", "ptt-" + stableId + ".json");
}
var backendId = nativeCat
    ? $"IcomLanSatellite:{nativeHost}:{Environment.GetEnvironmentVariable("SKYROOF_ICOM_LAN_PORT") ?? "50001"}"
    : $"{dialect}:{host}:{port}";
builder.Services.AddSingleton(new PttLeaseJournal(journal, backendId));
builder.Services.AddSingleton<NativeIcomCatSession>();
builder.Services.AddSingleton<IStationRadioCat>(p => nativeCat
    ? new NativeIcomCatBackend(p.GetRequiredService<NativeIcomCatSession>())
    : new RadioCatBackend(p.GetRequiredService<RigctldLink>(), dialect));
builder.Services.AddSingleton<StationDeviceService>();
builder.Services.AddSingleton<RotatorService>();
builder.Services.AddHostedService<PttLeaseMonitor>();
if (!string.IsNullOrWhiteSpace(nativeHost))
    builder.Services.AddHostedService<IcomLanReceiveWorker>();
var app = builder.Build();
app.MapGrpcService<StationDeviceService>();
app.MapGrpcService<RotatorService>();
if (!nativeCat)
    await app.Services.GetRequiredService<StationDeviceService>()
        .RecoverAfterRestartAsync(CancellationToken.None);
app.Logger.LogInformation("Device process attached to CAT {Host}:{Port} dialect {Dialect} via pipe {Pipe}",
    host, port, dialect, pipe);
await app.RunAsync();
