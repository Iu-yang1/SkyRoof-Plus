using System.Diagnostics;

namespace SkyRoof.Remote.Foundation.Timing;

public static class StationMonotonicClock
{
    public static long Now => Stopwatch.GetElapsedTime(0).Ticks * 100;
    public static long UtcNow => (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;
    public static StationClockAnchor Capture()
    {
        long before = Now;
        long utc = UtcNow;
        return new(utc, before + (Now - before) / 2);
    }

    public static async Task WaitUntilAsync(long target, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long remaining = target - Now;
            if (remaining <= 0) return;
            // Keep the final wait on the station monotonic clock.
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remaining / 1e6 - 2)), ct);
        }
    }
}
