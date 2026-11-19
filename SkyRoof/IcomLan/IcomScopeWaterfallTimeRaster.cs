namespace SkyRoof
{
  // Stable groups are anchored to the scan ring, not the moving viewport.
  internal sealed class IcomScopeWaterfallTimeRaster
  {
    private const int Points = IcomScopeWaterfallRing.Points;
    private readonly int[][] groups;
    private readonly int[] previousEnds;
    private int[][]? previous, published;

    internal IcomScopeWaterfallTimeRaster(int height)
    {
      groups = new int[height][];
      previousEnds = new int[height];
      Array.Fill(previousEnds, -1);
    }

    private static int Physical(int index, int count) => (count - index) % count;
    private static int Boundary(int group, int rows, int height) =>
      (group * rows + height - 1) / height;

    internal (int[][] Rows, int Head) Update(int[][] rows, int head)
    {
      int height = groups.Length;
      int phase = Physical(Math.Max(0, head), rows.Length);
      int newest = phase * height / rows.Length;
      bool changed = false;
      for (int g = 0; g < height; g++)
      {
        int first = Boundary(g, rows.Length, height);
        int end = Boundary(g + 1, rows.Length, height);
        if (g == newest) end = Math.Min(end, phase + 1);
        if (!GroupChanged(rows, g, first, end)) continue;
        groups[Physical(g, height)] = AverageColors(rows, first, end);
        previousEnds[g] = end;
        changed = true;
      }
      previous = rows;
      if (changed || published == null) published = (int[][])groups.Clone();
      return (published, Physical(newest, height));
    }

    private bool GroupChanged(int[][] rows, int group, int first, int end)
    {
      if (previous == null || previous.Length != rows.Length || previousEnds[group] != end) return true;
      for (int i = first; i < end; i++)
      {
        int index = Physical(i, rows.Length);
        if (!ReferenceEquals(previous[index], rows[index])) return true;
      }
      return false;
    }

    private static int[] AverageColors(int[][] rows, int first, int end)
    {
      int count = end - first;
      if (count == 1) return rows[Physical(first, rows.Length)];
      var result = new int[Points];
      for (int x = 0; x < Points; x++)
      {
        int red = 0, green = 0, blue = 0;
        for (int i = first; i < end; i++)
        {
          int argb = rows[Physical(i, rows.Length)][x];
          red += (argb >> 16) & 255;
          green += (argb >> 8) & 255;
          blue += argb & 255;
        }
        result[x] = unchecked((int)0xff000000) |
          ((red + count / 2) / count << 16) |
          ((green + count / 2) / count << 8) | ((blue + count / 2) / count);
      }
      return result;
    }
  }
}
