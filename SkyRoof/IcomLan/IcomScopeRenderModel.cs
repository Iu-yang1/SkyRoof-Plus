namespace SkyRoof
{
  internal sealed record IcomScopeRenderSnapshot(int Generation, IcomScopeFrame? Frame,
    byte[] Samples, byte[] Peak, bool PeakValid, int NoiseFloor, int Head, int[][] Colors);

  // Owned exclusively by the render worker. Published arrays are immutable.
  internal sealed class IcomScopeRenderModel
  {
    private const int Points = IcomScopeAssembler.ScopePointCount;
    private readonly byte[] samples = new byte[Points];
    private readonly byte[] peak = new byte[Points];
    private readonly Queue<byte[]> average = new();
    private readonly int[] sums = new int[Points];
    private readonly int[] colorMap = new int[161];
    private int[] palette = IcomLanSpectrumView.BuildPalette(IcomScopeWaterfallPalette.Classic);
    private byte[][] raw = [];
    private int[][] colors = [];
    private int[][] publishedColors = [];
    private int head = -1;
    private int generation;
    private int noiseFloor;
    private int averageCount = 1;
    private int brightness, contrast = 100;
    private bool peakEnabled, peakValid, dirty = true, rowsChanged = true, visible = true;
    private double shiftResidual;
    private IcomScopeFrame? frame, anchor;

    internal IcomScopeRenderModel() => SetHistoryRows(240);

    internal void SetHistoryRows(int rows)
    {
      if (raw.Length == rows) return;
      raw = Enumerable.Range(0, rows).Select(_ => new byte[Points]).ToArray();
      colors = new int[rows][];
      publishedColors = [];
      head = -1;
      anchor = null;
      shiftResidual = 0;
      dirty = rowsChanged = true;
    }

    internal void SetWaterfallVisible(bool value)
    {
      visible = value;
      if (value) dirty = true;
    }

    internal void SetDisplay(int offset, int gain, IcomScopeWaterfallPalette kind)
    {
      brightness = offset;
      contrast = gain;
      palette = IcomLanSpectrumView.BuildPalette(kind);
      dirty = true;
    }

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
      foreach (byte[] row in raw) Array.Clear(row);
      head = -1;
      anchor = null;
      noiseFloor = 0;
      shiftResidual = 0;
      ClearPeak();
      ResetAverage();
      dirty = true;
    }

    private void ResetAverage()
    {
      average.Clear();
      Array.Clear(sums);
    }

    internal void Push(IcomScopeFrame next)
    {
      if (next.Samples.Length < Points) return;
      if (IcomLanSpectrumView.RequiresHistoryReset(frame, next)) ResetHistory();
      if (next.SweepComplete) AdvanceGeometry(next);
      frame = next;
      if (next.SweepComplete)
      {
        int floor = IcomLanSpectrumView.EstimateNoiseFloor(next.Samples);
        noiseFloor = floor == 0 || noiseFloor == 0 ? floor : (3 * noiseFloor + floor + 2) / 4;
      }
      UpdateTrace(next);
      if (next.SweepComplete) AppendHistory(next.Samples);
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
      foreach (byte[] row in raw) Shift(row, shift);
      if (averageCount > 1) ResetAverage();
      if (peakValid) Shift(peak, shift);
      dirty = true;
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

    private void AppendHistory(byte[] input)
    {
      head = head < 0 ? 0 : (head + raw.Length - 1) % raw.Length;
      Array.Copy(input, raw[head], Points);
      if (!visible) dirty = true;
      if (dirty) return;
      BuildColorMap();
      colors[head] = Colorize(raw[head]);
      rowsChanged = true;
    }

    private void BuildColorMap() =>
      IcomLanSpectrumView.FillWaterfallColorMap(colorMap, palette, noiseFloor, brightness, contrast);

    private int[] Colorize(byte[] row)
    {
      var result = new int[Points];
      for (int x = 0; x < Points; x++) result[x] = colorMap[Math.Min(160, (int)row[x])];
      return result;
    }

    internal IcomScopeRenderSnapshot Snapshot()
    {
      if (visible && dirty)
      {
        BuildColorMap();
        for (int y = 0; y < raw.Length; y++) colors[y] = Colorize(raw[y]);
        dirty = false;
        rowsChanged = true;
      }
      if (visible && rowsChanged)
      {
        publishedColors = (int[][])colors.Clone();
        rowsChanged = false;
      }
      return new(generation, frame, (byte[])samples.Clone(), (byte[])peak.Clone(),
        peakValid, noiseFloor, head, visible ? publishedColors : []);
    }
  }
}
