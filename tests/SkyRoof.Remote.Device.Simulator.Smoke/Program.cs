using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
var dll = Path.GetFullPath(args.Single());
var pipeName = "skyroof-device-test-" + Guid.NewGuid().ToString("N");
var processInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
processInfo.ArgumentList.Add(dll);
processInfo.ArgumentList.Add(pipeName);
using var device = Process.Start(processInfo) ?? throw new InvalidOperationException("Cannot start Device Simulator.");
try
{
    using var handler = new SocketsHttpHandler
    {
        ConnectCallback = async (_, token) =>
        {
            var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.None);
            await stream.ConnectAsync(5000, token);
            return stream;
        }
    };
    using var channel = GrpcChannel.ForAddress("http://localhost",
        new GrpcChannelOptions { HttpHandler = handler });
    var client = new SimulatedDevice.SimulatedDeviceClient(channel);
    var initial = await client.ReadbackAsync(new SimReadbackRequest());
    Check(initial.ActualFrequencyHz == 435_650_000 && !initial.Transmitting);
    Check(initial.DeviceProcessId != Environment.ProcessId);
    var tune = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimTune,
        FrequencyHz = 432_080_000,
        CommandId = "frequency-a",
        SessionId = "A"
    });
    Check(tune.ActualFrequencyHz == 432_080_000 && tune.Revision > initial.Revision);
    var a = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimManualPttOn,
        CommandId = "a-tx", SessionId = "A"
    });
    Check(a.Transmitting && a.Result == "Assert");
    var b = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimManualPttOn,
        CommandId = "b-tx", SessionId = "B"
    });
    Check(b.Transmitting && b.Result == "Keep" && b.ActiveSessionId == "B");
    var stale = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimManualPttOff,
        CommandId = "a-tx", SessionId = "A"
    });
    Check(stale.Transmitting && stale.ActiveSessionId == "B");
    var release = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimManualPttOff,
        CommandId = "b-tx", SessionId = "B"
    });
    Check(!release.Transmitting && release.Result == "Release");
    var digital = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimDigitalTxOn,
        CommandId = "digital-slot-1", SessionId = "scheduler"
    });
    Check(digital.Transmitting);
    var takeover = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimManualPttOn,
        CommandId = "a-tx-2", SessionId = "A"
    });
    Check(takeover.DigitalAutomationPaused && takeover.Result == "Keep");
    var done = await client.ExecuteAsync(new SimDeviceRequest
    {
        Kind = SimCommandKind.SimManualPttOff,
        CommandId = "a-tx-2", SessionId = "A"
    });
    Check(!done.Transmitting && done.DigitalAutomationPaused);
    var readback = await client.ReadbackAsync(new SimReadbackRequest());
    Check(readback.Revision == done.Revision && readback.ActualFrequencyHz == 432_080_000);
    Console.WriteLine("PASS: separate simulated Device process, gRPC tune/readback/transaction/PTT and digital preemption.");
}
finally
{
    if (!device.HasExited) device.Kill(entireProcessTree: true);
    await device.WaitForExitAsync();
}

static void Check(bool good)
{
    if (!good) throw new InvalidOperationException("Cross-process simulated Device assertion failed.");
}
