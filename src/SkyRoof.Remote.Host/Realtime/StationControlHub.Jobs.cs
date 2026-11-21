namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationControlHub
{
    public async Task<RemoteTrackingJobStatus> StartTrackingJob(RemoteTrackingRequest request,
        int intervalMilliseconds, bool resumeAfterRestart)
    {
        await VerifyCurrentSessionAsync(requireOperator: true);
        return await device.StartTrackingJobAsync(request, intervalMilliseconds,
            resumeAfterRestart, Context.ConnectionAborted);
    }

    public async Task<RemoteTrackingJobStatus> GetTrackingJob()
    {
        await VerifyCurrentSessionAsync(requireOperator: false);
        return await device.ReadTrackingJobAsync(Context.ConnectionAborted);
    }

    public async Task<RemoteTrackingJobStatus> StopTrackingJob(string jobId)
    {
        await VerifyCurrentSessionAsync(requireOperator: true);
        return await device.StopTrackingJobAsync(jobId, Context.ConnectionAborted);
    }
}
