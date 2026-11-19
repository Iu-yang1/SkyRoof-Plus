using System.Diagnostics;

namespace SkyRoof
{
  // Source samples are immutable. Offset and clipping describe the portion
  // still visible after translating the RF axis, including direction reversals.
  internal readonly record struct IcomScopeWaterfallRow(byte[] Samples, int Offset,
    int First, int End, int Floor)
  {
    internal double SubBinOffset { get; init; }
    internal double EffectiveOffset => Offset + SubBinOffset;

    internal int At(int x)
    {
      int source = x - Offset;
      return source >= First && source < End ? Samples[source] : 0;
    }

    internal IcomScopeWaterfallRow TranslateFractional(double delta)
    {
      if (First == End || delta == 0) return this;
      double offset = EffectiveOffset + delta;
      // Keep partially visible interpolation support at the edge, but never
      // resurrect samples whose entire support has left the window.
      double nearest = Math.Round(offset);
      if (Math.Abs(offset - nearest) < 1e-9) offset = nearest;
      int first = Math.Max(First, (int)Math.Floor(-offset));
      int end = Math.Min(End, (int)Math.Ceiling(PointsLast + 1 - offset));
      if (end <= first) return this with { Offset = 0, SubBinOffset = 0, First = 0, End = 0 };
      int whole = (int)Math.Round(offset, MidpointRounding.AwayFromZero);
      return this with { Offset = whole, SubBinOffset = offset - whole, First = first, End = end };
    }

    private const int PointsLast = IcomScopeWaterfallRing.Points - 1;

    internal IcomScopeWaterfallRow Translate(int delta)
    {
      if (First == End || delta == 0) return this;
      int offset = Offset + delta;
      int first = Math.Max(First, -offset);
      int end = Math.Min(End, IcomScopeWaterfallRing.Points - offset);
      return end <= first
        ? this with { Offset = 0, First = 0, End = 0 }
        : this with { Offset = offset, First = first, End = end };
    }
  }

  internal sealed record IcomScopeWaterfallSnapshot(int Head, IcomScopeWaterfallRow[] Rows,
    int[] ColorTable, int[][] Colors)
  {
    internal long Epoch { get; init; }
    internal long SweepNumber { get; init; }
    internal long SweepTick { get; init; }
    internal double SweepPeriod { get; init; } = 1.0 / 30;
  }

  internal sealed class IcomScopeWaterfallRing
  {
    internal const int Points = IcomScopeAssembler.ScopePointCount;
    private const int Levels = 161;
    private static readonly byte[] Blank = new byte[Points];
    private IcomScopeWaterfallRow[] raw = [];
    private IcomScopeWaterfallRow[] published = [];
    private int[][] colors = [];
    private int[][] publishedColors = [];
    private int[] table = MakeColorTable(0, 100, IcomScopeWaterfallPalette.Classic);
    private bool changed = true, recolor = true, colorChanged = true;
    private bool cpuRaster = true, visible = true;
    private int head = -1;
    private long epoch, sweepNumber, sweepTick;
    private DateTime? lastCapture;
    private double sweepPeriod = 1.0 / 30;

    internal int Rows => raw.Length;

    internal void Resize(int rows)
    {
      raw = new IcomScopeWaterfallRow[rows];
      colors = new int[rows][];
      Clear();
    }

    internal void Clear()
    {
      Array.Fill(raw, new IcomScopeWaterfallRow(Blank, 0, 0, 0, 0));
      head = -1;
      epoch++;
      sweepNumber = sweepTick = 0;
      lastCapture = null;
      sweepPeriod = 1.0 / 30;
      changed = recolor = true;
    }

    internal void SetVisible(bool value)
    {
      visible = value;
      if (value) recolor = true;
    }

    internal void SetCpuRaster(bool value)
    {
      if (value == cpuRaster) return;
      cpuRaster = value;
      if (value) recolor = true;
      else publishedColors = [];
    }

    internal void SetDisplay(int brightness, int contrast, IcomScopeWaterfallPalette palette)
    {
      table = MakeColorTable(brightness, contrast, palette);
      recolor = true;
    }

    internal void Shift(int delta)
    {
      if (delta == 0) return;
      foreach (ref var row in raw.AsSpan())
        row = row.Translate(delta);
      changed = recolor = true;
    }

    internal void ShiftFractional(double delta)
    {
      if (delta == 0) return;
      foreach (ref var row in raw.AsSpan()) row = row.TranslateFractional(delta);
      changed = recolor = true;
    }

    internal void Append(byte[] samples, int floor, DateTime? captured = null)
    {
      if (captured is DateTime time)
      {
        if (lastCapture is DateTime previous)
        {
          double period = (time - previous).TotalSeconds;
          if (period is >= 0.008 and <= 0.25) sweepPeriod = 0.8 * sweepPeriod + 0.2 * period;
        }
        lastCapture = time;
      }
      sweepNumber++;
      sweepTick = Stopwatch.GetTimestamp();
      head = head < 0 ? 0 : (head + raw.Length - 1) % raw.Length;
      var row = new IcomScopeWaterfallRow(samples.AsSpan(0, Points).ToArray(), 0, 0, Points, floor);
      raw[head] = row;
      changed = true;
      if (!visible) recolor = true;
      if (!recolor && cpuRaster && visible)
      {
        colors[head] = Colorize(row);
        colorChanged = true;
      }
    }

    internal IcomScopeWaterfallSnapshot Snapshot()
    {
      if (visible && recolor)
      {
        int y = 0;
        var blanks = new Dictionary<int, int[]>();
        foreach (var row in raw)
        {
          if (cpuRaster)
            colors[y] = row.First == row.End ? BlankColor(row, blanks) : Colorize(row);
          y++;
        }
        recolor = false;
        changed = true;
        colorChanged = cpuRaster;
      }
      if (changed)
      {
        published = (IcomScopeWaterfallRow[])raw.Clone();
        changed = false;
      }
      if (colorChanged && cpuRaster && visible)
      {
        publishedColors = (int[][])colors.Clone();
        colorChanged = false;
      }
      return new(head, published, table, visible && cpuRaster ? publishedColors : [])
      { Epoch = epoch, SweepNumber = sweepNumber, SweepTick = sweepTick, SweepPeriod = sweepPeriod };
    }

    private int[] BlankColor(IcomScopeWaterfallRow row, Dictionary<int, int[]> cache)
    {
      if (!cache.TryGetValue(row.Floor, out var result))
      {
        result = Colorize(row);
        cache.Add(row.Floor, result);
      }
      return result;
    }

    private int[] Colorize(IcomScopeWaterfallRow row)
    {
      var result = new int[Points];
      int floor = Math.Clamp(row.Floor, 0, Levels - 1) * Levels;
      var lookup = table.AsSpan(floor, Levels);
      if (row.First == row.End)
      {
        Array.Fill(result, lookup[0]);
        return result;
      }
      if (row.SubBinOffset != 0)
      {
        ColorizeFractional(row, lookup, result);
        return result;
      }
      int first = row.First + row.Offset;
      int end = row.End + row.Offset;
      result.AsSpan(0, first).Fill(lookup[0]);
      result.AsSpan(end).Fill(lookup[0]);
      for (int x = first; x < end; x++)
        result[x] = lookup[Math.Min(Levels - 1, (int)row.Samples[x - row.Offset])];
      return result;
    }

    private static void ColorizeFractional(IcomScopeWaterfallRow row, ReadOnlySpan<int> lookup, Span<int> result)
    {
      double offset = -row.EffectiveOffset;
      int leftOffset = (int)Math.Floor(offset);
      int weight = (int)Math.Round((offset - leftOffset) * 256);
      int start = Math.Clamp(row.First - leftOffset - 1, 0, Points);
      int end = Math.Clamp(row.End - leftOffset, start, Points);
      result[..start].Fill(lookup[0]);
      result[end..].Fill(lookup[0]);
      for (int x = start; x < end; x++)
      {
        int left = x + leftOffset;
        int right = left + 1;
        int a = left >= row.First && left < row.End ? row.Samples[left] : 0;
        int b = right >= row.First && right < row.End ? row.Samples[right] : 0;
        result[x] = IcomScopeContinuousRaster.Blend(lookup[Math.Min(160, a)], lookup[Math.Min(160, b)], weight);
      }
    }

    private static int[] MakeColorTable(int brightness, int contrast, IcomScopeWaterfallPalette kind)
    {
      int[] palette = IcomLanSpectrumView.BuildPalette(kind);
      var result = new int[Levels * Levels];
      for (int floor = 0; floor < Levels; floor++)
        IcomLanSpectrumView.FillWaterfallColorMap(result.AsSpan(floor * Levels, Levels),
          palette, floor, brightness, contrast);
      return result;
    }
  }
}
