using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

internal static class NativeDeviceCatProbe
{
    public static async Task RunAsync(string pipe, FakeNativeIcomRadio fake, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await client.ConnectAsync(3000, token); return client; }
                catch { await client.DisposeAsync(); throw; }
            }
        };
        using var channel = GrpcChannel.ForAddress("http://localhost",
            new GrpcChannelOptions { HttpHandler = handler });
        var cat = new StationDevice.StationDeviceClient(channel);
        var initial = await AwaitReadyAsync(cat, ct);
        Check(initial.RxFrequencyHz == 145_900_000 && initial.TxFrequencyHz == 435_650_000,
            "Native SAT readback did not distinguish MAIN/SUB.");
        var rx = await SendAsync(cat, new() { Operation = DeviceOperation.DeviceRxFrequency,
            FrequencyHz = 145_920_000 }, ct);
        Check(rx.RxFrequencyHz == 145_920_000 && rx.TxFrequencyHz == 435_650_000,
            "Native downlink tuning changed uplink.");
        var tx = await SendAsync(cat, new() { Operation = DeviceOperation.DeviceTxFrequency,
            FrequencyHz = 435_550_000 }, ct);
        Check(tx.RxFrequencyHz == 145_920_000 && tx.TxFrequencyHz == 435_550_000,
            "Native uplink tuning changed downlink.");
        await SendAsync(cat, new() { Operation = DeviceOperation.DeviceTxMode, Mode = "USB-D" }, ct);
        await CheckPttAsync(cat, fake, ct);
        await NativeTimedTxProbe.RunAsync(cat, fake, ct);
        await NativeTimedTxInterruptionProbe.RunAsync(cat, ct);
        await NativeCwProbe.RunAsync(cat, fake, ct);
        Console.WriteLine("PASS: independent native satellite CAT via Device IPC; MAIN/SUB and PTT ownership.");
    }

    private static async Task<DeviceSnapshot> AwaitReadyAsync(
        StationDevice.StationDeviceClient cat, CancellationToken ct)
    {
        while (true)
        {
            try { return await cat.ReadbackAsync(new DeviceReadbackRequest(),
                deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct); }
            catch (RpcException error) when (error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            { await Task.Delay(100, ct); }
        }
    }

    private static async Task CheckPttAsync(StationDevice.StationDeviceClient cat, FakeNativeIcomRadio fake, CancellationToken ct)
    {
        var begin = new DeviceCommand { Operation = DeviceOperation.DeviceManualBegin,
            TransactionId = "native-test", SessionId = "operator", FrequencyHz = 435_550_000,
            Mode = "USB-D", AudioSource = "LAN" };
        var asserted = await SendAsync(cat, begin, ct);
        Check(asserted.ActualPtt && asserted.ActiveTransactionId == begin.TransactionId,
            "Native PTT assertion not owned.");
        await NativeTxOwnershipProbe.RunAsync(cat, fake, begin, asserted, ct);
        begin.TransactionId = "native-takeover";
        begin.SessionId = "operator-2";
        begin.Operation = DeviceOperation.DeviceManualEnd;
        var released = await SendAsync(cat, begin, ct);
        Check(!released.ActualPtt && released.ActiveTransactionId.Length == 0,
            "Native PTT release not confirmed.");
        await NativeTxOwnershipProbe.AssertRejectedAsync(cat, begin, released.TransactionEpoch, ct);
    }

    private static async Task<DeviceSnapshot> SendAsync(StationDevice.StationDeviceClient cat,
        DeviceCommand command, CancellationToken ct) =>
        await cat.ExecuteAsync(command, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: ct);

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
