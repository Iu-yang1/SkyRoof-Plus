using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
var pipe = args.Length == 1 ? args[0] : "skyroof-simulated-device";
var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.ConfigureKestrel(server =>
    server.ListenNamedPipe(pipe, listen => listen.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc();
builder.Services.AddSingleton<SimulatedDeviceService>();
var app = builder.Build();
app.MapGrpcService<SimulatedDeviceService>();
Console.WriteLine("SIMULATED DEVICE ONLY, no hardware I/O; pipe=" + pipe);
await app.RunAsync();
