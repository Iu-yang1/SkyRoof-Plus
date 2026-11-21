using System.Diagnostics;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using SkyRoof.Remote.Ipc;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("The Named Pipe gRPC probe is for Windows.");
var pipeName = args.Length == 1 ? args[0] : "skyroof-remote-ipc-probe";
var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.ConfigureKestrel(server =>
    server.ListenNamedPipe(pipeName, listen => listen.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc();
var app = builder.Build();
app.MapGrpcService<StationProbeService>();
Console.WriteLine("StationProbe listening at named pipe " + pipeName);
await app.RunAsync();

public sealed class StationProbeService : StationProbe.StationProbeBase
{
    public override Task<PingReply> Ping(PingRequest request, ServerCallContext context) =>
        Task.FromResult(new PingReply
        {
            Nonce = request.Nonce,
            Epoch = request.Epoch,
            ServerProcessId = Environment.ProcessId,
            ServerMonotonicTicks = Stopwatch.GetTimestamp()
        });
}
