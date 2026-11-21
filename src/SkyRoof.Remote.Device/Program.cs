using Microsoft.AspNetCore.Server.Kestrel.Core;
using SkyRoof.Remote.Device;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Named Pipe Device runtime.");
var pipe = Environment.GetEnvironmentVariable("SKYROOF_DEVICE_PIPE") ?? "skyroof-station-device";
var host = Environment.GetEnvironmentVariable("SKYROOF_CAT_HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_CAT_PORT"), out var selected)
    ? selected : 4532;
var dialect = string.Equals(Environment.GetEnvironmentVariable("SKYROOF_CAT_DIALECT"),
    "rigctld", StringComparison.OrdinalIgnoreCase) ? CatDialect.Rigctld : CatDialect.SkyCat;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
    server.ListenNamedPipe(pipe, options => options.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc();
builder.Services.AddSingleton(new RigctldLink(host, port));
builder.Services.AddSingleton(p => new RadioCatBackend(p.GetRequiredService<RigctldLink>(), dialect));
builder.Services.AddSingleton<StationDeviceService>();
var app = builder.Build();
app.MapGrpcService<StationDeviceService>();
app.Logger.LogInformation("Device process attached to CAT {Host}:{Port} dialect {Dialect} via pipe {Pipe}",
    host, port, dialect, pipe);
await app.RunAsync();
