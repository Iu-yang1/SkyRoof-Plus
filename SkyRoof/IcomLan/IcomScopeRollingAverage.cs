namespace SkyRoof
{
  // The historical samples remain aligned to the same RF bins as the
  // current waterfall. Missing edge bins are excluded from the divisor.
  internal sealed class IcomScopeRollingAverage
  {
    private const int Points = IcomScopeWaterfallRing.Points;
    private readonly Queue<IcomScopeWaterfallRow> rows = new();
    private readonly int[] sums = new int[Points];
    private readonly int[] counts = new int[Points];

    internal int Count => rows.Count;

    internal void Clear()
    {
      rows.Clear();
      Array.Clear(sums);
      Array.Clear(counts);
    }

    internal void Shift(int delta)
    {
      if (delta == 0 || rows.Count == 0) return;
      Array.Clear(sums);
      Array.Clear(counts);
      int count = rows.Count;
      for (int i = 0; i < count; i++)
      {
        var row = rows.Dequeue().Translate(delta);
        rows.Enqueue(row);
        Accumulate(row, 1);
      }
    }

    internal void Append(byte[] input, int capacity, Span<byte> output)
    {
      var row = new IcomScopeWaterfallRow(input.AsSpan(0, Points).ToArray(), 0, 0, Points, 0);
      rows.Enqueue(row);
      Accumulate(row, 1);
      while (rows.Count > capacity) Accumulate(rows.Dequeue(), -1);
      for (int x = 0; x < Points; x++)
        output[x] = counts[x] == 0 ? input[x] :
          (byte)Math.Clamp((int)Math.Round(sums[x] / (double)counts[x]), 0, 160);
    }

    private void Accumulate(IcomScopeWaterfallRow row, int direction)
    {
      for (int src = row.First; src < row.End; src++)
      {
        int x = src + row.Offset;
        sums[x] += direction * row.Samples[src];
        counts[x] += direction;
      }
    }
  }
}
