namespace SkyRoof.Remote.Host.Realtime;

internal static class Q65TrackingFrequency
{
    internal static async Task<long> ResolveAsync(StationDeviceBridge station, string? jobId,
        long manualHz, long targetUtcNanoseconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return manualHz;
        var prediction = await station.PredictTrackingJobAtAsync(jobId, targetUtcNanoseconds / 1_000_000, ct);
        if (prediction.TxHz <= 0 || prediction.CelestialTarget != "MOON")
            throw new InvalidOperationException("Selected Moon job produced no uplink.");
        return prediction.TxHz;
    }
}
