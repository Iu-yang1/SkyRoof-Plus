using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;

internal static class DesktopPttChecks
{
    internal static async Task RunAsync(string pipe, FakeRigctld radio)
    {
        using var a = new StationIpcClient(pipe);
        using var b = new StationIpcClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var initialOn = radio.Count("T 1");
        var initialOff = radio.Count("T 0");
        var aTx = Guid.NewGuid().ToString("N");
        var bTx = Guid.NewGuid().ToString("N");
        try
        {
            var first = await a.ManualPttAsync(DeviceOperation.DeviceManualBegin, aTx, token);
            Require(a.OwnsPtt(first, aTx) && radio.Count("T 1") == initialOn + 1, "Desktop did not assert PTT.");
            var renewed = await a.ManualPttAsync(DeviceOperation.DeviceManualHeartbeat, aTx, token);
            Require(a.OwnsPtt(renewed, aTx) && renewed.StatusText == "manual-ptt-lease-renewed", "Desktop heartbeat not associated with lease.");
            var next = await b.ManualPttAsync(DeviceOperation.DeviceManualBegin, bTx, token);
            Require(b.OwnsPtt(next, bTx) && radio.Count("T 1") == initialOn + 1, "Desktop takeover toggled RF.");
            var stale = await a.ManualPttAsync(DeviceOperation.DeviceManualEnd, aTx, token);
            Require(b.OwnsPtt(stale, bTx) && radio.Count("T 0") == initialOff, "Stale desktop release interrupted current owner.");
            var released = await b.ReleaseSessionAsync(token);
            Require(!released.ActualPtt && radio.Count("T 0") == initialOff + 1, "Desktop session release did not return RX.");
            Console.WriteLine("PASS: desktop manual PTT, transaction heartbeat, no-RF-transition takeover, stale release and session RX.");
        }
        finally
        {
            await a.ReleaseSessionAsync(CancellationToken.None);
            await b.ReleaseSessionAsync(CancellationToken.None);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
