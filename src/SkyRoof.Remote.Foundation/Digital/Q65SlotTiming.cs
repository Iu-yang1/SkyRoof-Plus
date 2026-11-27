namespace SkyRoof.Remote.Foundation.Digital;

public static class Q65SlotTiming
{
    public static long Select(long nowNs, int seconds, int txStartSamples12k, long delayMs, bool odd, long minimumSlot = 0)
    {
        if (seconds is not (15 or 30 or 60 or 120 or 300) || txStartSamples12k < 0)
            throw new ArgumentException("Invalid Q65 slot geometry.");
        long period = seconds * 1_000_000_000L;
        long offset = txStartSamples12k * 1_000_000_000L / 12000 - checked(delayMs * 1_000_000);
        long earliest = checked(nowNs + 3_000_000_000L);
        long slot = Math.Max(minimumSlot, (earliest - offset + period - 1) / period);
        if ((slot % 2 == 1) != odd) slot++;
        return slot;
    }

    public static long Target(long slot, int seconds, int startSamples12k, long delayMs) =>
        checked(slot * seconds * 1_000_000_000L + startSamples12k * 1_000_000_000L / 12000 - delayMs * 1_000_000);
}
