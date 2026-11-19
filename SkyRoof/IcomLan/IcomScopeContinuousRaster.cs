using System.Numerics;

namespace SkyRoof
{
  // Fixed-width box filter in continuous sweep coordinates. Each actual
  // row occupies [age, age+1). Reserve one row for the presentation delay.
  internal static class IcomScopeContinuousRaster
  {
    private const int Points = IcomScopeWaterfallRing.Points;
    private const int MaskRB = 0x00ff00ff, MaskG = 0x0000ff00;
    private const int Weight = 256;

    internal static int Blend(int a, int b, int weight)
    {
      int rb = unchecked((a & MaskRB) * (Weight - weight) + (b & MaskRB) * weight + 0x00800080);
      int g = (a & MaskG) * (Weight - weight) + (b & MaskG) * weight + 0x00008000;
      return unchecked((int)0xff000000) | ((rb >>> 8) & MaskRB) | ((g >>> 8) & MaskG);
    }

    internal static void Render(int[][] rows, int head, double lag, Span<int> pixels, int height)
    {
      double step = (rows.Length - 1d) / height;
      lag = Math.Clamp(lag, 0, 1);
      head = Math.Max(0, head);
      for (int y = 0; y < height; y++)
        RenderRow(rows, head, (lag + y * step, lag + (y + 1) * step),
          pixels.Slice(y * Points, Points));
    }

    private static void RenderRow(int[][] rows, int head, (double Start, double End) span, Span<int> output)
    {
      int first = Math.Clamp((int)Math.Floor(span.Start), 0, rows.Length - 1);
      int last = Math.Clamp((int)Math.Ceiling(span.End) - 1, first, rows.Length - 1);
      if (first == last)
      {
        rows[(head + first) % rows.Length].AsSpan().CopyTo(output);
        return;
      }
      Span<int> weights = stackalloc int[last - first + 1];
      int assigned = 0;
      for (int age = first; age <= last; age++)
      {
        int cumulative = age == last ? Weight : Math.Clamp((int)Math.Round(
          (Math.Min(age + 1d, span.End) - span.Start) / (span.End - span.Start) * Weight), 0, Weight);
        weights[age - first] = cumulative - assigned;
        assigned = cumulative;
      }
      Filter(rows, (head + first) % rows.Length, weights, output);
    }

    private static void Filter(int[][] rows, int start, ReadOnlySpan<int> weights, Span<int> output)
    {
      int x = 0;
      if (Vector.IsHardwareAccelerated)
      {
        for (; x <= Points - Vector<int>.Count; x += Vector<int>.Count)
        {
          var rb = Vector<int>.Zero;
          var g = Vector<int>.Zero;
          for (int i = 0; i < weights.Length; i++)
          {
            var value = new Vector<int>(rows[(start + i) % rows.Length], x);
            rb += (value & new Vector<int>(MaskRB)) * weights[i];
            g += (value & new Vector<int>(MaskG)) * weights[i];
          }
          var result = new Vector<int>(unchecked((int)0xff000000)) |
            (((rb + new Vector<int>(0x00800080)) >>> 8) & new Vector<int>(MaskRB)) |
            (((g + new Vector<int>(0x00008000)) >>> 8) & new Vector<int>(MaskG));
          result.CopyTo(output.Slice(x));
        }
      }
      for (; x < Points; x++)
      {
        int rb = 0, g = 0;
        for (int i = 0; i < weights.Length; i++)
        {
          int c = rows[(start + i) % rows.Length][x];
          rb = unchecked(rb + (c & MaskRB) * weights[i]);
          g += (c & MaskG) * weights[i];
        }
        output[x] = unchecked((int)0xff000000) |
          ((unchecked(rb + 0x00800080) >>> 8) & MaskRB) | (((g + 0x8000) >>> 8) & MaskG);
      }
    }
  }
}
