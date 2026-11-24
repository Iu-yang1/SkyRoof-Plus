using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Device;

internal sealed partial class IcomLanReceiveWorker
{
    private async Task RunAttachedSessionAsync(IcomLanCivSocket civ, IcomLanAudioSocket audio,
        IcomLanControlConnection control, IcomLanAudioCodec codec, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bool native = string.Equals(Environment.GetEnvironmentVariable("SKYROOF_CAT_DIALECT"),
            "icom-lan-satellite", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (native)
            {
                var radio = control.Capabilities ?? throw new IOException("Missing radio capabilities.");
                catSession.Attach(new IcomLanSatelliteCat(civ, radio.CivAddress));
                await device.RecoverAfterRestartAsync(ct);
            }
            if (string.Equals(Environment.GetEnvironmentVariable("SKYROOF_ICOM_LAN_RF_SCOPE"),
                "true", StringComparison.OrdinalIgnoreCase))
                await civ.EnableRfScopeAsync(control.Capabilities, ct);
            await SuperviseMediaAsync(civ, audio, control, codec, lifetime);
        }
        finally
        {
            if (native)
            {
                catSession.Detach();
                await device.InvalidateNativeSessionAsync();
            }
        }
    }

    private async Task SuperviseMediaAsync(IcomLanCivSocket civ, IcomLanAudioSocket audio,
        IcomLanControlConnection control, IcomLanAudioCodec codec, CancellationTokenSource lifetime)
    {
        var pump = RunAudioToMediaAsync(audio, (int)control.NegotiatedRxRate,
            codec, lifetime.Token);
        var scope = RunScopeToBusAsync(civ, lifetime.Token);
        var finished = await Task.WhenAny(pump, scope, audio.MaintenanceTask,
            control.MaintenanceTask, civ.MaintenanceTask, civ.QueryFailureTask);
        try { await finished; }
        finally
        {
            lifetime.Cancel();
            if (finished != pump)
                await StopMediaPumpAsync(pump, lifetime.Token);
            if (finished != scope)
                await StopMediaPumpAsync(scope, lifetime.Token);
        }
    }
    private async Task StopMediaPumpAsync(Task pump, CancellationToken ct)
    {
        try { await pump; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogDebug("Native Icom media pump stopped with its session.");
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Native Icom media pump failed while closing its session.");
        }
    }
}
