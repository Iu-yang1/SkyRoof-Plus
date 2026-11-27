using SkyRoof.CW;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    private async Task<ActiveCw> StartCwAsync(string message, string sessionId, CancellationToken ct)
    {
        await _commands.WaitAsync(ct);
        try
        {
            await RecoverOrphanedPttAsync(ct);
            if (_owner is not null || _cwActive is not null)
                throw new InvalidOperationException("Transmitter is owned by another transaction.");
            var keyer = await ConnectCwAsync(ct);
            try
            {
                var status = await keyer.GetStatusAsync(ct);
                if (!status.ReadyToSend) throw new InvalidOperationException(
                    $"CW not ready: {status.Lease}, {status.Mode}, BK-IN {status.BreakIn}, TX {status.Transmitting}.");
                string id = (keyer is NativeCwKeyer ? "native-cw:" : "cw:") + Guid.NewGuid().ToString("N");
                if (keyer is NativeCwKeyer) journal.Save(id, sessionId);
                await keyer.SendAsync(message, ct);
                var active = new ActiveCw(keyer, sessionId, id, CwMessageTiming.ComputeWatchdog(message, status.KeySpeedRaw));
                _cwActive = active;
                ++_epoch;
                ++_revision;
                return active;
            }
            catch { await keyer.DisposeAsync(); throw; }
        }
        finally { _commands.Release(); }
    }

    private async Task StopCwLockedAsync(ActiveCw active)
    {
        if (!ReferenceEquals(_cwActive, active)) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            await active.Keyer.StopAsync(timeout.Token);
            if (active.Keyer is NativeCwKeyer)
                await FinishNativeCwAsync(timeout.Token);
        }
        finally
        {
            try { await active.Keyer.DisposeAsync(); }
            finally
            {
                _cwActive = null;
                ++_epoch;
                ++_revision;
                active.Stopped.TrySetResult();
            }
        }
    }
}
