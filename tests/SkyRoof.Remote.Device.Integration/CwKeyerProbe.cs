using Grpc.Core;
using SkyRoof.Remote.Ipc;

internal static class CwKeyerProbe
{
    public static async Task RunAsync(StationDevice.StationDeviceClient client, FakeRigctld rig)
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Command(client, DeviceOperation.DeviceTxMode, ct.Token, mode: "CW");
        var speed = await client.SetCwSpeedAsync(new CwSpeedRequest { Wpm = 25 }, cancellationToken: ct.Token);
        Check(speed.KeySpeedRaw == 115 && Math.Abs(speed.Wpm - 25) < 0.1 && speed.BreakIn == 1,
            "CW speed not verified against raw radio setting.");
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct.Token))
        {
            using var send = client.SendCwAsync(new CwSendRequest { Text = "CQ TEST K1ABC K", SessionId = "cw-a" },
                cancellationToken: cancel.Token);
            await WaitForAsync(() => rig.Count("SEND CQ TEST K1ABC K") == 1, ct.Token);
            var tracked = await client.ExecuteAsync(new DeviceCommand { Operation = DeviceOperation.DeviceTrackFrequencies,
                FrequencyHz = 145_910_000, TxFrequencyHz = 435_551_000 }, cancellationToken: ct.Token);
            Check(tracked.StatusText == "tracking-cw-tx-deferred" && rig.Count("I 435551000") == 0 &&
                rig.Count("F 145910000") == 1, "CW tracking failed to defer TX only.");
            var busy = await Command(client, DeviceOperation.DeviceDigitalBegin, ct.Token);
            Check(busy.StatusText == "digital-busy", "Digital TX stole active CW.");
            cancel.Cancel();
            try { await send.ResponseAsync; }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled) { }
            await WaitForAsync(() => rig.Count("STOP") == 1, ct.Token);
        }
        await WaitForRxAsync(client, ct.Token);
        using var next = client.SendCwAsync(new CwSendRequest { Text = "CQ TEST W1XYZ K", SessionId = "cw-b" },
            cancellationToken: ct.Token);
        await WaitForAsync(() => rig.Count("SEND CQ TEST W1XYZ K") == 1, ct.Token);
        var manual = await Command(client, DeviceOperation.DeviceManualBegin, ct.Token, mode: "USB");
        Check(manual.ActualPtt && rig.Count("STOP") == 2, "Manual PTT failed to stop CW first.");
        Check((await next.ResponseAsync).State == "interrupted", "CW receipt did not report preemption.");
        Check((await client.ReadbackAsync(new DeviceReadbackRequest(), cancellationToken: ct.Token)).ActualPtt,
            "Old CW cleanup released manual owner.");
        await Command(client, DeviceOperation.DeviceManualEnd, ct.Token);
        await WaitForRxAsync(client, ct.Token);
        Console.WriteLine("PASS: real TCP CW keyer, verified WPM, cancellation STOP, tracking defer, digital arbitration, manual preemption and stale cleanup.");
    }

    private static async Task<DeviceSnapshot> Command(StationDevice.StationDeviceClient client,
        DeviceOperation operation, CancellationToken ct, string mode = "") =>
        await client.ExecuteAsync(new DeviceCommand { Operation = operation,
            TransactionId = "cw-probe-manual", SessionId = "cw-probe", Mode = mode },
            cancellationToken: ct);

    private static async Task WaitForRxAsync(StationDevice.StationDeviceClient client, CancellationToken ct)
    {
        while ((await client.ReadbackAsync(new DeviceReadbackRequest(), cancellationToken: ct)).ActualPtt)
            await Task.Delay(25, ct);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition()) await Task.Delay(20, ct);
    }

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidDataException(reason);
    }
}
