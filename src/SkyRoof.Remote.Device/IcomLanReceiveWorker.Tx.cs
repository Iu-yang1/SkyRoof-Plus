namespace SkyRoof.Remote.Device;

internal sealed partial class IcomLanReceiveWorker
{
    private async Task RunTxPumpAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (await timer.WaitForNextTickAsync(ct))
            await device.TickNativeTxAsync(ct);
    }
}
