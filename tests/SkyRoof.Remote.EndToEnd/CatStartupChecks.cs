using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Host.Realtime;

internal static class CatStartupChecks
{
    // Host readiness only signals that HTTP and Identity have started. The Station
    // and Device IPC endpoints can come online later. Retrying this READ-ONLY
    // check is safe; RF-changing control commands must never be retried here.
    internal static async Task<RemoteCatStatus> WaitReadbackAsync(HubConnection hub)
    {
        var started = Stopwatch.StartNew();
        HubException? latest = null;
        while (started.Elapsed < TimeSpan.FromSeconds(25))
        {
            try { return await hub.InvokeAsync<RemoteCatStatus>("GetCatReadback"); }
            catch (HubException ex) when (
                ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                latest = ex;
                await Task.Delay(200);
            }
        }
        throw new TimeoutException("Station/Device did not become ready within 25 s.", latest);
    }
}
