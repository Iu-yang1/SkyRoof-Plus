using Grpc.Core;
using SkyRoof.Remote.Ipc;

internal static class NativeCwProbe
{
    public static async Task RunAsync(StationDevice.StationDeviceClient cat, FakeNativeIcomRadio radio, CancellationToken ct)
    {
        await ModeAsync(cat, "CW-R", ct);
        var speed = await cat.SetCwSpeedAsync(new CwSpeedRequest { Wpm = 25 }, cancellationToken: ct);
        if (speed.KeySpeedRaw != 115 || speed.Mode != "CW-R" || speed.BreakIn != 1 ||
            speed.ActualTxFrequencyHz != 435_550_000)
            throw new InvalidDataException("Native CW mode, frequency, BK-IN or WPM readback mismatch.");

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var call = cat.SendCwAsync(new CwSendRequest { Text = "CQ K1ABC K", SessionId = "native-cw-probe" },
            cancellationToken: cancel.Token);
        var active = await WaitAsync(cat, state => state.ActualPtt && state.ActiveSessionId == "native-cw-probe", ct);
        if (radio.LastCw != "CQ K1ABC K" || !active.ActiveTransactionId.StartsWith("native-cw:"))
            throw new InvalidDataException("Native Command 17 payload or ownership incorrect.");
        var busy = await cat.GetCwStatusAsync(new CwStatusRequest(), cancellationToken: ct);
        if (busy.Lease != "BUSY" || !busy.Transmitting)
            throw new InvalidDataException("Native CW active status incorrect.");
        cancel.Cancel();
        try { await call.ResponseAsync; throw new InvalidDataException("Cancelled CW completed."); }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled) { }
        await WaitAsync(cat, state => !state.ActualPtt && state.ActiveSessionId.Length == 0, ct);
        if (radio.CwStops != 1) throw new InvalidDataException("Native CW STOP not acknowledged once.");
        await ModeAsync(cat, "USB-D", ct);
        Console.WriteLine("PASS: real authenticated native UDP CI-V CW-R, WPM BCD, BK-IN, exact Command 17 text, STOP and RX.");
    }

    private static async Task ModeAsync(StationDevice.StationDeviceClient cat, string mode, CancellationToken ct) =>
        await cat.ExecuteAsync(new DeviceCommand { Operation = DeviceOperation.DeviceTxMode, Mode = mode }, cancellationToken: ct);

    private static async Task<DeviceSnapshot> WaitAsync(StationDevice.StationDeviceClient cat,
        Func<DeviceSnapshot, bool> match, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        while (true)
        {
            var state = await cat.ReadbackAsync(new DeviceReadbackRequest(), cancellationToken: timeout.Token);
            if (match(state)) return state;
            await Task.Delay(30, timeout.Token);
        }
    }
}
