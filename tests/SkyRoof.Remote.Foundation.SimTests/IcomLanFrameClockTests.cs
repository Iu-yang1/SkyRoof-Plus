using SkyRoof.Remote.Foundation.IcomLan;

internal static class IcomLanFrameClockTests
{
    public static void Run()
    {
        static byte[] Block(int samples) => new byte[samples * 2];
        var clock = new IcomLanRxFrameClock();
        var now = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        var a = clock.Push(30, Block(480), now);
        Check(a.Count == 1 && a[0].FirstSampleIndex == 0);
        Check(clock.Push(32, Block(480), now.AddMilliseconds(10)).Count == 0);
        var restored = clock.Push(31, Block(320), now.AddMilliseconds(20));
        Check(restored.Count == 2 &&
            restored[0].FirstSampleIndex == 480 &&
            restored[1].FirstSampleIndex == 800,
            "Reordered packet with variable size assigned wrong source indices.");
        Check(clock.Push(32, Block(480), now.AddMilliseconds(25)).Count == 0);
        Check(clock.Push(34, Block(480), now.AddMilliseconds(30)).Count == 0);
        var skipped = clock.Flush(now.AddMilliseconds(160));
        Check(skipped.Count == 1 && skipped[0].FirstSampleIndex == 1760,
            "Missing packet did not advance audio sample timeline.");
        var continued = clock.Push(35, Block(400), now.AddMilliseconds(180));
        Check(continued.Count == 1 && continued[0].FirstSampleIndex == 2240);
        var wrapped = new IcomLanRxFrameClock();
        Check(wrapped.Push(65535, Block(480), now)[0].FirstSampleIndex == 0);
        Check(wrapped.Push(0, Block(480), now)[0].FirstSampleIndex == 480);
        Console.WriteLine("PASS: negotiated Icom RX sample clock reorders variable UDP frames and accounts for missing samples.");
    }

    private static void Check(bool condition, string message = "Icom source clock assertion failed.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
