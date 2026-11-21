using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using SkyRoof.Remote.Station;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Station Core uses Windows Named Pipes.");
var stationPipe = Environment.GetEnvironmentVariable("SKYROOF_STATION_PIPE")
    ?? "skyroof-station-core";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
    k.ListenNamedPipe(stationPipe, options => options.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc();
builder.Services.AddSingleton<DeviceControlForwarder>();
var app = builder.Build();
app.MapGrpcService<DeviceControlForwarder>();
app.Logger.LogInformation("Station Core listening on {Pipe}", stationPipe);
await app.RunAsync();
