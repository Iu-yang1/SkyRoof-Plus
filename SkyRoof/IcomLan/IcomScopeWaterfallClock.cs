using System.Diagnostics;

namespace SkyRoof
{
  // One real sweep of presentation latency permits interpolation between
  // observed positions. No future RF samples or extrapolated colors.
  internal sealed class IcomScopeWaterfallClock
  {
    private long epoch = -1;
    private double position;
    internal double Lag(IcomScopeWaterfallSnapshot history, long now, bool held = false)
    {
      if (history.Head < 0 || history.SweepNumber == 0)
      {
        epoch = -1;
        return 0;
      }
      if (epoch != history.Epoch)
      {
        epoch = history.Epoch;
        position = history.SweepNumber - 1;
      }
      double elapsed = Math.Max(0, now - history.SweepTick) / (double)Stopwatch.Frequency;
      double target = history.SweepNumber - 1 + Math.Clamp(elapsed / history.SweepPeriod, 0, 1);
      if (!held) position = Math.Max(position, target);
      // Never move backwards, even when the next sweep arrives early.
      // A late or missing input settles at the newest real position.
      position = Math.Clamp(position, history.SweepNumber - 1, history.SweepNumber);
      return history.SweepNumber - position;
    }
  }
}
