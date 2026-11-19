namespace SkyRoof
{
  internal sealed record IcomScopeRenderSnapshot(int Generation, long Sequence, long ZoomRevision, IcomScopeFrame? Frame,
    byte[] Samples, byte[] Peak, bool PeakValid, int NoiseFloor, int Head, int[][] Colors, IcomScopeWaterfallSnapshot Waterfall);

  // Owned exclusively by the render worker. Published arrays are immutable.
  internal sealed class IcomScopeRenderModel
  {
    private const int Points = IcomScopeAssembler.ScopePointCount;
    private readonly byte[] samples = new byte[Points];
    private readonly byte[] peak = new byte[Points];
    private readonly Queue<byte[]> average = new();
    private readonly int[] sums = new int[Points];
    private readonly IcomScopeWaterfallRing history = new();
    private int generation;
    private long sequence, zoomRevision;
    private int noiseFloor;
    private int averageCount = 1;
    private bool peakEnabled, peakValid;
    private double shiftResidual;
    private IcomScopeFrame? frame, anchor;

    internal IcomScopeRenderModel() => SetHistoryRows(240);

    internal void SetHistoryRows(int rows)
    {
      if (history.Rows == rows) return;
      history.Resize(rows);
      anchor = null;
      shiftResidual = 0;
    }

    internal void SetWaterfallVisible(bool value) => history.SetVisible(value);
    internal void SetCpuRaster(bool enabled) => history.SetCpuRaster(enabled);
    internal void SetDisplay(int offset, int gain, IcomScopeWaterfallPalette kind) =>
      history.SetDisplay(offset, gain, kind);

    internal void SetAverage(int count)
    {
      averageCount = count;
      ResetAverage();
    }

    internal void SetPeak(bool enabled)
    {
      peakEnabled = enabled;
      if (enabled) ClearPeak();
    }

    internal void ClearPeak()
    {
      Array.Clear(peak);
      peakValid = false;
    }

    internal void Clear(int nextGeneration)
    {
      generation = nextGeneration;
      frame = null;
      Array.Clear(samples);
      ResetHistory();
    }

    private void ResetHistory()
    {
      history.Clear();
      anchor = null;
      noiseFloor = 0;
      shiftResidual = 0;
      ClearPeak();
      ResetAverage();
    }

    private void ResetAverage()
    {
      average.Clear();
      Array.Clear(sums);
    }

    internal void Push(IcomScopeFrame next)
    {
      if (next.Samples.Length < Points) return;
      if (IcomLanSpectrumView.RequiresHistoryReset(frame, next))
      {
        zoomRevision++;
        ResetHistory();
      }
      if (next.SweepComplete) AdvanceGeometry(next);
      frame = next;
      if (next.SweepComplete)
      {
        int floor = IcomLanSpectrumView.EstimateNoiseFloor(next.Samples);
        noiseFloor = floor == 0 || noiseFloor == 0 ? floor : (3 * noiseFloor + floor + 2) / 4;
      }
      UpdateTrace(next);
      if (next.SweepComplete) history.Append(next.Samples, noiseFloor);
    }

    private void AdvanceGeometry(IcomScopeFrame next)
    {
      if (anchor != null && !IcomLanSpectrumView.RequiresHistoryReset(anchor, next))
      {
        var before = anchor.Geometry;
        var after = next.Geometry;
        if (before.IsValid && after.IsValid && before.SpanHz == after.SpanHz &&
            next.Mode is (byte)IcomScopeMode.Center or (byte)IcomScopeMode.ScrollCenter)
        {
          int shift = IcomLanSpectrumView.CalculateHistoryShiftBins(before, after, shiftResidual, out double residual);
          if (Math.Abs(shift) > Points / 10) ResetHistory();
          else
          {
            shiftResidual = residual;
            if (shift != 0) ShiftHistory(shift);
          }
          if (before.LowerFrequencyHz != after.LowerFrequencyHz) ResetAverage();
        }
      }
      anchor = next;
    }

    private void ShiftHistory(int shift)
    {
      history.Shift(shift);
      if (averageCount > 1) ResetAverage();
      if (peakValid) Shift(peak, shift);
    }

    private static void Shift(byte[] row, int shift)
    {
      if (shift > 0)
      {
        Array.Copy(row, 0, row, shift, Points - shift);
        Array.Clear(row, 0, shift);
      }
      else
      {
        int left = -shift;
        Array.Copy(row, left, row, 0, Points - left);
        Array.Clear(row, Points - left, left);
      }
    }

    private void UpdateTrace(IcomScopeFrame next)
    {
      if (averageCount <= 1 || (!next.SweepComplete && average.Count == 0))
        Array.Copy(next.Samples, samples, Points);
      else if (next.SweepComplete) AddAverage(next.Samples);
      if (!peakEnabled || (averageCount > 1 && !next.SweepComplete)) return;
      for (int i = 0; i < Points; i++)
        if (!peakValid || samples[i] > peak[i]) peak[i] = samples[i];
      peakValid = true;
    }

    private void AddAverage(byte[] input)
    {
      var copy = input.AsSpan(0, Points).ToArray();
      average.Enqueue(copy);
      for (int i = 0; i < Points; i++) sums[i] += copy[i];
      if (average.Count > averageCount)
      {
        var old = average.Dequeue();
        for (int i = 0; i < Points; i++) sums[i] -= old[i];
      }
      for (int i = 0; i < Points; i++)
        samples[i] = (byte)Math.Clamp((int)Math.Round(sums[i] / (double)average.Count), 0, 160);
    }

    internal IcomScopeRenderSnapshot Snapshot()
    {
      var waterfall = history.Snapshot(noiseFloor);
      return new(generation, ++sequence, zoomRevision, frame, (byte[])samples.Clone(), (byte[])peak.Clone(),
        peakValid, noiseFloor, waterfall.Head, waterfall.Colors, waterfall);
    }
  }
}
