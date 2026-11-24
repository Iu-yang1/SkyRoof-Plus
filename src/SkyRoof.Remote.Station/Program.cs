using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using SkyRoof.Remote.Station;
using SkyRoof.Orbit.Contracts;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Station Core uses Windows Named Pipes.");
var stationPipe = Environment.GetEnvironmentVariable("SKYROOF_STATION_PIPE")
    ?? "skyroof-station-core";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
    k.ListenNamedPipe(stationPipe, options => options.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc(options => options.MaxReceiveMessageSize = 16 * 1024 * 1024);
builder.Services.AddSingleton<DeviceControlForwarder>();
builder.Services.AddSingleton<IOrbitBackend>(_ => StationOrbitBackend.Create());
builder.Services.AddSingleton<OrbitTrackingCalculator>();
builder.Services.AddSingleton<TrackingService>();
builder.Services.AddSingleton<StationCatalogCache>();
builder.Services.AddSingleton<TrackingJobStore>();
builder.Services.AddSingleton<TrackingJobRunner>();
builder.Services.AddHostedService(p => p.GetRequiredService<TrackingJobRunner>());
var app = builder.Build();
if (!string.Equals(Environment.GetEnvironmentVariable("SKYROOF_ORBIT_BACKEND"),
    "sgpnet", StringComparison.OrdinalIgnoreCase))
    await StationOrekitEopBootstrap.RefreshAsync(StationOrbitBackend.DataDirectory(), app.Logger);

app.Logger.LogInformation("Station orbit backend: {Backend}", app.Services.GetRequiredService<IOrbitBackend>().BackendId);
app.MapGrpcService<DeviceControlForwarder>();
app.MapGrpcService<TrackingService>();
app.MapGrpcService<StationCatalogService>();
app.MapGrpcService<TrackingJobsService>();
app.MapGrpcService<RotatorControlForwarder>();
app.Logger.LogInformation("Station Core listening on {Pipe}", stationPipe);
await app.RunAsync();
