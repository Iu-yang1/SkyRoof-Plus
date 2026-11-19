using System.Diagnostics;

namespace SkyRoof
{
  // UI-thread-owned, bounded and allocation-free on the paint path.
  // Distinguish genuinely new waveform paints from overlay-only redraws.
  internal sealed class IcomScopePaintMetrics
  {
    private const int Capacity = 240;
    private readonly long[] paint = new long[Capacity];
    private readonly long[] intervals = new long[Capacity];
    private int paintIndex, intervalIndex, paintCount, intervalCount;
    private long lastFrameUtc, lastFreshTick;
    private double lastAgeMs;
    internal long FreshPaintCount { get; private set; }

    internal void Record(long endTick, long durationTicks, long frameUtc, long nowUtc)
    {
      if (frameUtc <= 0) return;
      Add(paint, ref paintIndex, ref paintCount, durationTicks);
      if (frameUtc == lastFrameUtc) return;
      if (lastFreshTick != 0)
        Add(intervals, ref intervalIndex, ref intervalCount, endTick - lastFreshTick);
      lastFreshTick = endTick;
      lastFrameUtc = frameUtc;
      lastAgeMs = Math.Max(0, (nowUtc - frameUtc) / (double)TimeSpan.TicksPerMillisecond);
      FreshPaintCount++;
    }

    internal void ResetWindow()
    {
      paintIndex = intervalIndex = paintCount = intervalCount = 0;
      lastFrameUtc = lastFreshTick = 0;
      lastAgeMs = 0;
    }

    private static void Add(long[] values, ref int index, ref int count, long value)
    {
      values[index] = Math.Max(0, value);
      index = (index + 1) % values.Length;
      count = Math.Min(values.Length, count + 1);
    }

    internal readonly record struct Summary(double PaintP95Ms, double PaintP99Ms,
      double IntervalP95Ms, double IntervalP99Ms, double IntervalMaxMs, double LastAgeMs);

    internal Summary Snapshot()
    {
      Span<long> p = stackalloc long[paintCount];
      Span<long> i = stackalloc long[intervalCount];
      paint.AsSpan(0, paintCount).CopyTo(p);
      intervals.AsSpan(0, intervalCount).CopyTo(i);
      p.Sort();
      i.Sort();
      return new(Percentile(p, 0.95), Percentile(p, 0.99),
        Percentile(i, 0.95), Percentile(i, 0.99), Percentile(i, 1), lastAgeMs);
    }

    private static double Percentile(ReadOnlySpan<long> data, double fraction)
    {
      if (data.IsEmpty) return 0;
      int index = Math.Clamp((int)Math.Ceiling(data.Length * fraction) - 1,
        0, data.Length - 1);
      return 1000.0 * data[index] / Stopwatch.Frequency;
    }
  }
}
