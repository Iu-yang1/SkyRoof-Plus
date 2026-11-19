namespace SkyRoof
{
  // Source samples are immutable. Offset and clipping describe the portion
  // still visible after translating the RF axis, including direction reversals.
  internal readonly record struct IcomScopeWaterfallRow(byte[] Samples, int Offset,
    int First, int End, int Floor)
  {
    internal int At(int x)
    {
      int source = x - Offset;
      return source >= First && source < End ? Samples[source] : 0;
    }
  }

  internal sealed record IcomScopeWaterfallSnapshot(int Head, IcomScopeWaterfallRow[] Rows,
    int[] ColorTable, int[][] Colors);

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
      for (int i = 0; i < raw.Length; i++)
      {
        var row = raw[i];
        if (row.First == row.End) continue;
        int offset = row.Offset + delta;
        int first = Math.Max(row.First, -offset);
        int end = Math.Min(row.End, Points - offset);
        raw[i] = end <= first
          ? new(Blank, 0, 0, 0, row.Floor)
          : row with { Offset = offset, First = first, End = end };
      }
      changed = recolor = true;
    }

    internal void Append(byte[] samples, int floor)
    {
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

    internal IcomScopeWaterfallSnapshot Snapshot(int floor)
    {
      if (visible && recolor)
      {
        for (int y = 0; y < raw.Length; y++)
        {
          raw[y] = raw[y] with { Floor = floor };
          if (cpuRaster) colors[y] = Colorize(raw[y]);
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
      return new(head, published, table, visible && cpuRaster ? publishedColors : []);
    }

    private int[] Colorize(IcomScopeWaterfallRow row)
    {
      var result = new int[Points];
      int floor = Math.Clamp(row.Floor, 0, Levels - 1) * Levels;
      for (int x = 0; x < Points; x++)
        result[x] = table[floor + Math.Min(Levels - 1, row.At(x))];
      return result;
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
