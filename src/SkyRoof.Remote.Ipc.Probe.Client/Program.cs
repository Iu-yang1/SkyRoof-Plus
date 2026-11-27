using System.Diagnostics;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("The Named Pipe gRPC probe is for Windows.");
var pipeName = args.Length == 1 ? args[0] : "skyroof-remote-ipc-probe";
using var handler = new SocketsHttpHandler
{
    ConnectCallback = async (_, cancellationToken) =>
    {
        var stream = LocalPipeClient.Create(pipeName);
        try
        {
            await stream.ConnectAsync(2000, cancellationToken);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }
};
using var channel = GrpcChannel.ForAddress("http://localhost",
    new GrpcChannelOptions { HttpHandler = handler });
var client = new StationProbe.StationProbeClient(channel);
const int samples = 128;
var latencies = new double[samples];
for (var i = 0; i < samples; i++)
{
    var nonce = Guid.NewGuid().ToString("N");
    var stopwatch = Stopwatch.StartNew();
    var reply = await client.PingAsync(new PingRequest { Nonce = nonce, Epoch = 42 },
        deadline: DateTime.UtcNow.AddSeconds(5));
    stopwatch.Stop();
    if (reply.Nonce != nonce || reply.Epoch != 42 || reply.ServerProcessId == Environment.ProcessId
        || reply.ServerMonotonicTicks <= 0)
        throw new InvalidOperationException("Named Pipe cross-process RPC response invalid.");
    latencies[i] = stopwatch.Elapsed.TotalMilliseconds;
}
Array.Sort(latencies);
var median = latencies[samples / 2];
var p95 = latencies[(int)Math.Ceiling(samples * .95) - 1];
Console.WriteLine($"PASS: {samples} cross-process gRPC/Named Pipe RPCs. P50={median:F3}ms P95={p95:F3}ms");
