using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;

internal static class DesktopCwChecks
{
    internal static async Task RunAsync(string pipe, FakeRigctld radio)
    {
        using var client = new StationIpcClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        var original = await client.ReadRadioAsync(token);
        var initial = await client.ReadCwAsync(token);
        try
        {
            await client.SetRadioAsync(DeviceOperation.DeviceTxMode, 0, "CW", token);
            var speed = await client.SetCwSpeedAsync(25, token);
            Require(speed.KeySpeedRaw == 115, "Desktop CW WPM quantization mismatch.");
            var read = await client.ReadCwAsync(token);
            Require(read.KeySpeedRaw == speed.KeySpeedRaw && read.Mode == "CW", "Desktop CW readback mismatch.");
            var sendCount = radio.Count("SEND EE");
            var stopCount = radio.Count("STOP");
            var receipt = await client.SendCwAsync("EE", token);
            Require(receipt.State == "watchdog-elapsed" && radio.Count("SEND EE") == sendCount + 1 &&
                radio.Count("STOP") == stopCount + 1, "Desktop CW message did not complete and stop.");
            await VerifyInterruptedAsync(client, radio, token);
            Console.WriteLine("PASS: desktop CW WPM readback, actual TCP keyer SEND, completion and owner-only session STOP.");
        }
        finally
        {
            await client.ReleaseSessionAsync(CancellationToken.None);
            await client.SetCwSpeedAsync(initial.Wpm, CancellationToken.None);
            await client.SetRadioAsync(DeviceOperation.DeviceTxMode, 0, original.TxMode, CancellationToken.None);
        }
    }

    private static async Task VerifyInterruptedAsync(StationIpcClient client, FakeRigctld radio, CancellationToken token)
    {
        const string message = "CQ CQ CQ CQ CQ";
        var count = radio.Count("SEND " + message);
        var stops = radio.Count("STOP");
        var pending = client.SendCwAsync(message, token);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (radio.Count("SEND " + message) == count && clock.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(25, token);
        Require(radio.Count("SEND " + message) == count + 1, "Desktop CW send never reached keyer.");
        var released = await client.ReleaseSessionAsync(token);
        var receipt = await pending;
        Require(!released.ActualPtt && receipt.State == "interrupted" && radio.Count("STOP") == stops + 1,
            "Desktop CW session release did not interrupt actual keyer.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
