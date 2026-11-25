using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Host.Realtime;

internal static class MoonJobPredictionChecks
{
    internal static async Task RunAsync(HubConnection hub, RemoteTrackingRequest original, FakeRigctld radio)
    {
        var request = original with { ElevationMaskDegrees = -90 };
        var job = await hub.InvokeAsync<RemoteTrackingJobStatus>("StartTrackingJob", request, 1000, false);
        try
        {
            long utc = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds();
            int ptt = radio.Count("T 1");
            var direct = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request with { UtcUnixMilliseconds = utc });
            var bound = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTrackingJobAt", job.JobId, utc);
            if (bound.TxHz != direct.TxHz || bound.RxHz != direct.RxHz ||
                bound.UtcUnixMilliseconds != utc || radio.Count("T 1") != ptt)
                throw new InvalidDataException("Bound Moon TX prediction changed frequency, epoch or keyed PTT.");
        }
        finally { await hub.InvokeAsync<RemoteTrackingJobStatus>("StopTrackingJob", job.JobId); }
        try
        {
            await hub.InvokeAsync<RemoteTrackingStatus>("PredictTrackingJobAt", job.JobId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            throw new InvalidDataException("Stopped Moon job remained available for Q65 frequency prediction.");
        }
        catch (HubException error) when (error.Message.Contains("stopped or replaced", StringComparison.Ordinal)) { }
        Console.WriteLine("PASS: selected Moon job predicts scheduled UTC TX/RX and rejects stale stopped job without PTT.");
    }
}
