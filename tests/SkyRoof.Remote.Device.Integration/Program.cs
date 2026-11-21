using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
await using var rig = new FakeRigctld();
var pipe = "skyroof-real-device-" + Guid.NewGuid().ToString("N");
var commandLine = new ProcessStartInfo("dotnet") { UseShellExecute = false };
commandLine.ArgumentList.Add(Path.GetFullPath(args.Single()));
commandLine.Environment["SKYROOF_DEVICE_PIPE"] = pipe;
commandLine.Environment["SKYROOF_CAT_HOST"] = "127.0.0.1";
commandLine.Environment["SKYROOF_CAT_PORT"] = rig.Port.ToString();
commandLine.Environment["SKYROOF_CAT_DIALECT"] = "skycat";
var journalFile = Path.Combine(Path.GetTempPath(), "skyroof-lease-" + Guid.NewGuid().ToString("N") + ".json");
commandLine.Environment["SKYROOF_DEVICE_PTT_JOURNAL"] = journalFile;
using var device = Process.Start(commandLine)
    ?? throw new InvalidOperationException("CAT Device service failed to start.");
try
{
    using var handler = new SocketsHttpHandler
    {
        ConnectCallback = async (_, ct) =>
        {
            var stream = new NamedPipeClientStream(".", pipe,
                PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.None);
            await stream.ConnectAsync(3000, ct);
            return stream;
        }
    };
    using var channel = GrpcChannel.ForAddress("http://localhost",
        new GrpcChannelOptions { HttpHandler = handler });
    var client = new StationDevice.StationDeviceClient(channel);
    DeviceSnapshot? initial = null;
    for (var i = 0; i < 30; i++)
    {
        try { initial = await client.ReadbackAsync(new DeviceReadbackRequest(),
                deadline: DateTime.UtcNow.AddSeconds(3)); break; }
        catch (RpcException) { await Task.Delay(150); }
    }
    Check(initial is { ActualPtt: false, RxFrequencyHz: 145900000, TxFrequencyHz: 435650000 },
        "Initial CAT readback incorrect");
    if (initial is null || initial.DeviceProcessId == Environment.ProcessId)
        throw new InvalidOperationException("Service did not run in separate process");

    async Task<DeviceSnapshot> Send(DeviceOperation kind, string id = "tx-a", string session = "A",
        long hz = 0, string mode = "", string source = "") =>
        await client.ExecuteAsync(new DeviceCommand {
            Operation = kind, TransactionId = id, SessionId = session, FrequencyHz = hz,
            Mode = mode, AudioSource = source
        }, deadline: DateTime.UtcNow.AddSeconds(8));

    var rx = await Send(DeviceOperation.DeviceRxFrequency, hz: 145_920_000);
    Check(rx.RxFrequencyHz == 145_920_000 && rx.TxFrequencyHz == 435_650_000,
        "RX tuning incorrectly changed TX frequency");
    var tx = await Send(DeviceOperation.DeviceTxFrequency, hz: 435_550_000);
    Check(tx.TxFrequencyHz == 435_550_000 && tx.RxFrequencyHz == 145_920_000,
        "TX tuning incorrectly changed downlink");
    Check(rig.Count("F 145920000") == 1 && rig.Count("I 435550000") == 1,
        "SkyCAT duplex CAT F/I mapping incorrect");
    await Send(DeviceOperation.DeviceRxMode, mode: "USB");
    await Send(DeviceOperation.DeviceTxMode, mode: "LSB");
    Check(rig.Count("M USB 0") == 1 && rig.Count("X LSB 0") == 1,
        "SkyCAT duplex mode mapping incorrect");

    var a = await Send(DeviceOperation.DeviceManualBegin, hz: 435_550_000, mode: "LSB", source: "LAN");
    Check(File.Exists(journalFile), "Owned PTT lease was not persisted.");
    Check(a.ActualPtt && a.ActiveSessionId == "A" && rig.Count("T 1") == 1,
        "First keyed transmission not confirmed");
    var repeated = await Send(DeviceOperation.DeviceManualBegin, hz: 435_550_000,
        mode: "LSB", source: "LAN");
    Check(repeated.StatusText == "duplicate" && rig.Count("T 1") == 1, "Idempotent PTT ON failed");
    var takeover = await Send(DeviceOperation.DeviceManualBegin, "tx-b", "B",
        435_550_000, "LSB", "LAN");
    Check(takeover.ActualPtt && takeover.StatusText == "source-takeover-no-rf-transition"
        && takeover.ActiveSessionId == "B" && rig.Count("T 1") == 1
        && rig.Count("T 0") == 0, "Same-configuration PTT takeover toggled RF");
    var stale = await Send(DeviceOperation.DeviceManualEnd, "tx-a", "A");
    Check(stale.ActualPtt && stale.ActiveSessionId == "B"
        && rig.Count("T 0") == 0, "Stale PTT OFF released new operator");

    var reconfigured = await Send(DeviceOperation.DeviceManualBegin, "tx-c", "C",
        435_560_000, "USB", "LAN");
    Check(reconfigured.StatusText == "rf-reconfigured" && reconfigured.ActualPtt
        && rig.Count("T 0") == 1 && rig.Count("T 1") == 2
        && rig.Count("I 435560000") == 1 && rig.Count("X USB 0") == 1,
        "Configuration change did not create a new RF transaction");
    var released = await Send(DeviceOperation.DeviceManualEnd, "tx-c", "C");
    Check(!File.Exists(journalFile), "PTT journal survived a confirmed release.");
    Check(!released.ActualPtt && released.StatusText == "ptt-released"
        && rig.Count("T 0") == 2, "Original owner key-up did not return RX");

    var digital = await Send(DeviceOperation.DeviceDigitalBegin, "slot-1", "scheduler");
    Check(digital.ActualPtt, "Scheduled digital TX did not assert");
    var interrupted = await Send(DeviceOperation.DeviceManualBegin, "tx-d", "D",
        435_560_000, "USB", "LAN");
    Check(interrupted.ActualPtt && interrupted.DigitalAutomationPaused,
        "Manual PTT did not cancel digital auto TX");
    await Send(DeviceOperation.DeviceManualEnd, "tx-d", "D");
    var paused = await Send(DeviceOperation.DeviceDigitalBegin, "slot-2", "scheduler");
    Check(!paused.ActualPtt && paused.StatusText == "digital-busy",
        "Digital automatic TX resumed despite default pause");

    var enabled = await Send(DeviceOperation.DeviceResumeDigitalTx);
    Check(!enabled.DigitalAutomationPaused, "Authorized digital resume failed.");
    var scheduled = await Send(DeviceOperation.DeviceDigitalBegin, "slot-2", "scheduler");
    Check(scheduled.ActualPtt, "Digitals did not resume after explicit authorization.");
    await Send(DeviceOperation.DeviceDigitalEnd, "slot-2", "scheduler");

    var held = await Send(DeviceOperation.DeviceManualBegin, "session-tx", "web-1");
    Check(held.ActualPtt, "New session failed to assert PTT.");
    var unrelated = await client.ReleaseSessionAsync(new DeviceReleaseSessionRequest {
        SessionId = "web-2"
    });
    Check(unrelated.ActualPtt && unrelated.ActiveSessionId == "web-1",
        "Unrelated disconnected session released live operator.");
    var disconnected = await client.ReleaseSessionAsync(new DeviceReleaseSessionRequest {
        SessionId = "web-1"
    });
    Check(!disconnected.ActualPtt && disconnected.StatusText == "session-released",
        "Disconnected owner did not release hardware PTT.");

    rig.RejectNext("T 1");
    try
    {
        await Send(DeviceOperation.DeviceManualBegin, "denied", "D");
        throw new InvalidOperationException("Rejected hardware PTT was incorrectly accepted");
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) { }
    Check(!(await client.ReadbackAsync(new DeviceReadbackRequest())).ActualPtt,
        "PTT reject left hardware transmitting");

    var expectedReleases = rig.Count("T 0");
    rig.SetExternalPtt(true);
    try
    {
        await Send(DeviceOperation.DeviceManualBegin, "external", "E");
        throw new InvalidOperationException("Device claimed external PTT");
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition) { }
    var stop = await Send(DeviceOperation.DeviceEmergencyRx);
    Check(stop.ActualPtt && rig.Count("T 0") == expectedReleases,
        "STOP should not release a PTT transmission the Device process does not own");
    rig.SetExternalPtt(false);
    var crashTxn = await Send(DeviceOperation.DeviceManualBegin, "crash-tx", "crash-session");
    Check(crashTxn.ActualPtt && File.Exists(journalFile),
        "Crash simulation did not establish an owned PTT grant.");
    var stopBeforeCrash = rig.Count("T 0");
    device.Kill(entireProcessTree: true);
    await device.WaitForExitAsync();

    using var restarted = Process.Start(commandLine)
        ?? throw new InvalidOperationException("Device did not restart.");
    try
    {
        using var nextHandler = new SocketsHttpHandler {
            ConnectCallback = async (_, ct) => {
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                await stream.ConnectAsync(3000, ct);
                return stream;
            }
        };
        using var nextChannel = GrpcChannel.ForAddress("http://localhost",
            new GrpcChannelOptions { HttpHandler = nextHandler });
        var nextClient = new StationDevice.StationDeviceClient(nextChannel);
        DeviceSnapshot? recovered = null;
        for (var i = 0; i < 40; i++)
        {
            try {
                recovered = await nextClient.ReadbackAsync(new DeviceReadbackRequest(),
                    deadline: DateTime.UtcNow.AddSeconds(3));
                break;
            }
            catch (RpcException) { await Task.Delay(150); }
        }
        Check(recovered is { ActualPtt: false, DigitalAutomationPaused: true },
            "New Device process failed to release previously owned radio TX.");
        Check(recovered!.ActiveTransactionId == "" &&
            rig.Count("T 0") == stopBeforeCrash + 1 &&
            !File.Exists(journalFile), "Restart left an orphaned RF transaction.");
    }
    finally
    {
        if (!restarted.HasExited) restarted.Kill(entireProcessTree: true);
        await restarted.WaitForExitAsync();
    }
    Console.WriteLine("PASS: orphaned hardware PTT released on real Device process restart.");
    Console.WriteLine("PASS: independent CAT Device service, SkyCAT duplex F/I and M/X, readback, PTT handover, pause, NACK, external PTT.");
}
finally
{
    if (!device.HasExited) device.Kill(entireProcessTree: true);
    await device.WaitForExitAsync();
    if (File.Exists(journalFile)) File.Delete(journalFile);
}

static void Check(bool condition, string explanation)
{
    if (!condition) throw new InvalidOperationException(explanation);
}
