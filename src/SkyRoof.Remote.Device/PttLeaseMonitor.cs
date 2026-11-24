using System.Diagnostics;

namespace SkyRoof.Remote.Device;

public sealed class PttLeaseMonitor(
    StationDeviceService service, ILogger<PttLeaseMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var raw = Environment.GetEnvironmentVariable("SKYROOF_PTT_LEASE_TIMEOUT_MS");
        var threshold = int.TryParse(raw, out var configured) && configured > 0
            ? TimeSpan.FromMilliseconds(configured)
            : TimeSpan.FromSeconds(15);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    if (await service.ExpireManualLeaseAsync(threshold, stoppingToken))
                        logger.LogWarning("Manually keyed CAT PTT released because session heartbeat expired.");
                }
                catch (Exception e) when (e is IOException or TimeoutException or System.Net.Sockets.SocketException)
                {
                    logger.LogWarning(e, "Failed to release expired PTT lease; retrying.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
