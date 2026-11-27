using System.Diagnostics;

namespace SkyRoof.Remote.Supervisor;

public sealed partial class ManagedProcessSupervisor
{
    private static async Task ShutdownChildrenAsync(
        Child[] children, ILogger<ManagedProcessSupervisor> logger)
    {
        foreach (var child in children.Where(x =>
            !string.Equals(x.Spec.Name, "Device", StringComparison.OrdinalIgnoreCase)))
            await StopChildAsync(child.Process, logger);

        foreach (var child in children.Where(x =>
            string.Equals(x.Spec.Name, "Device", StringComparison.OrdinalIgnoreCase)))
        {
            if (child.Process is { HasExited: false })
            {
                var pipe = child.Spec.Environment.GetValueOrDefault("SKYROOF_DEVICE_PIPE")
                    ?? Environment.GetEnvironmentVariable("SKYROOF_DEVICE_PIPE")
                    ?? "skyroof-station-device";
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    if (!await DeviceShutdown.ReleaseOwnedPttAsync(pipe, timeout.Token))
                        logger.LogWarning("Device PTT release was not confirmed by CAT readback.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Device PTT release failed during Supervisor shutdown.");
                }
                if (child.Spec.Environment.TryGetValue("SKYROOF_ROTATOR_HOST", out var rotatorHost)
                    && !string.IsNullOrWhiteSpace(rotatorHost))
                {
                    try
                    {
                        using var motorTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                        await DeviceShutdown.StopRotatorAsync(pipe, motorTimeout.Token);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Rotator STOP could not be confirmed during shutdown.");
                    }
                }
            }
            await StopChildAsync(child.Process, logger);
        }
    }

    private static async Task StopChildAsync(Process? process, ILogger logger)
    {
        if (process is null) return;
        using (process)
        {
            try
            {
                if (process.HasExited) return;
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                logger.LogError(error, "Managed child did not complete shutdown; PID {Pid}.", process.Id);
            }
        }
    }
}
