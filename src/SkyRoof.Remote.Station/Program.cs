using SkyRoof.Remote.Ipc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using SkyRoof.Remote.Station;
using SkyRoof.Orbit.Contracts;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Station Core uses Windows Named Pipes.");
var stationPipe = Environment.GetEnvironmentVariable("SKYROOF_STATION_PIPE")
    ?? "skyroof-station-core";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseNamedPipes(options =>
{
    var security = WindowsPipeAccessPolicy.Create("SKYROOF_STATION_PIPE_CLIENT_SIDS");
    options.CurrentUserOnly = security is null;
    if (security is not null) options.PipeSecurity = security;
});
builder.WebHost.ConfigureKestrel(k =>
    k.ListenNamedPipe(stationPipe, options => options.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc(options => options.MaxReceiveMessageSize = 32 * 1024 * 1024);
builder.Services.AddSingleton<DeviceControlForwarder>();
builder.Services.AddSingleton<DspControlForwarder>();
builder.Services.AddSingleton<StationSdrService>();
builder.Services.AddSingleton<IOrbitBackend>(_ => StationOrbitBackend.Create());
builder.Services.AddSingleton<OrbitTrackingCalculator>();
builder.Services.AddSingleton<TrackingService>();
builder.Services.AddSingleton<StationCatalogCache>();
builder.Services.AddSingleton<TrackingJobStore>();
builder.Services.AddSingleton<StationRuntimeService>();
builder.Services.AddSingleton<StationLogbookService>();
builder.Services.AddSingleton<StationRecordingWorker>();
builder.Services.AddHostedService(p => p.GetRequiredService<StationRecordingWorker>());
builder.Services.AddSingleton<TrackingJobRunner>();
builder.Services.AddHostedService(p => p.GetRequiredService<TrackingJobRunner>());
builder.Services.AddHostedService<IssOemRefreshService>();
builder.Services.AddHostedService<StationCatalogRefreshService>();
var app = builder.Build();
if (!string.Equals(Environment.GetEnvironmentVariable("SKYROOF_ORBIT_BACKEND"),
    "sgpnet", StringComparison.OrdinalIgnoreCase))
    await StationOrekitEopBootstrap.RefreshAsync(StationOrbitBackend.DataDirectory(), app.Logger);

app.Logger.LogInformation("Station orbit backend: {Backend}", app.Services.GetRequiredService<IOrbitBackend>().BackendId);
app.MapGrpcService<DeviceControlForwarder>();
app.MapGrpcService<DspControlForwarder>();
app.MapGrpcService<StationSdrService>();
app.MapGrpcService<StationSignalService>();
app.MapGrpcService<StationRecorderService>();
app.MapGrpcService<TrackingService>();
app.MapGrpcService<StationCatalogService>();
app.MapGrpcService<TrackingJobsService>();
app.MapGrpcService<StationRuntimeService>();
app.MapGrpcService<StationLogbookService>();
app.MapGrpcService<StationAfcService>();
app.MapGrpcService<RotatorControlForwarder>();
app.Logger.LogInformation("Station Core listening on {Pipe}", stationPipe);
await app.RunAsync();
