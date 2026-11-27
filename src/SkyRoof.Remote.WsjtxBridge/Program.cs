using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.WsjtxBridge;

if (args.Length == 0)
{
    Console.WriteLine("Usage: SkyRoof.Remote.WsjtxBridge <station-url> [port] [RX output index] [TX input index]");
    Console.WriteLine("Set SKYROOF_REMOTE_TOKEN in the client environment. With no audio indexes: CAT only.");
    return 2;
}
if (args[0] == "--list-audio")
{
    WindowsPcmBridge.ListDevices();
    return 0;
}
var uri = new Uri(args[0].TrimEnd('/') + "/");
int port = args.Length > 1 ? int.Parse(args[1]) : 4534;
bool withAudio = args.Length == 4;
if (args.Length is not (1 or 2 or 4)) throw new ArgumentException("Specify both RX output and TX input.");
string token = Environment.GetEnvironmentVariable("SKYROOF_REMOTE_TOKEN")
    ?? throw new InvalidOperationException("SKYROOF_REMOTE_TOKEN is required.");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
await using var hub = new HubConnectionBuilder()
    .WithUrl(new Uri(uri, "hub/control"), options => options.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .Build();
await hub.StartAsync(stop.Token);
hub.Closed += error => { stop.Cancel(); return Task.CompletedTask; };
var rig = new RemoteRig(hub);
await using var ptt = new DigitalPttSession(rig, withAudio);
await using var audio = withAudio ? new WindowsPcmBridge(rig, ptt, int.Parse(args[2]), int.Parse(args[3])) : null;
var tasks = new List<Task>
{
    new RigctlListener(rig, ptt, port).RunAsync(stop.Token),
    ptt.HeartbeatAsync(stop.Token)
};
if (audio is not null) tasks.Add(audio.RunAsync(uri, token, stop.Token));
try { await await Task.WhenAny(tasks); }
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
finally
{
    stop.Cancel();
    try { await Task.WhenAll(tasks); }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
}
return 0;
