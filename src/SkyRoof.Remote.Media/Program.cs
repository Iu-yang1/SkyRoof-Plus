using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using SkyRoof.Remote.Media;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Media IPC requires Windows Named Pipes.");

var pipe = Environment.GetEnvironmentVariable("SKYROOF_MEDIA_PIPE") ?? "skyroof-station-media";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
    server.ListenNamedPipe(pipe, endpoint => endpoint.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc();
builder.Services.AddSingleton<RxAudioPublisher>();
builder.Services.AddSingleton<MediaIngressService>();
var app = builder.Build();
_ = app.Services.GetRequiredService<RxAudioPublisher>();
app.MapGrpcService<MediaIngressService>();
app.Logger.LogInformation("Media Service ready on {Pipe}", pipe);
await app.RunAsync();
