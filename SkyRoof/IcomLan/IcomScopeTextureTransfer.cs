namespace SkyRoof
{
  // One byte per source bin. A single pad byte makes every row DWORD aligned.
  internal static class IcomScopeTextureTransfer
  {
    internal const int Points = IcomScopeWaterfallRing.Points;
    internal const int RowBytes = (Points + 3) & ~3;
    internal const int RowWords = RowBytes / sizeof(int);

    internal static int[] Pack(IcomScopeWaterfallRow[] rows)
    {
      var result = new int[rows.Length * RowWords];
      for (int y = 0; y < rows.Length; y++)
        Buffer.BlockCopy(rows[y].Samples, 0, result, y * RowBytes, Points);
      return result;
    }

    internal static void PackRow(IcomScopeWaterfallRow row, int[] target) =>
      Buffer.BlockCopy(row.Samples, 0, target, 0, Points);

    internal static bool RawChanged(IcomScopeWaterfallRow before, IcomScopeWaterfallRow after) =>
      !ReferenceEquals(before.Samples, after.Samples);

    internal static bool MetadataChanged(IcomScopeWaterfallRow before, IcomScopeWaterfallRow after) =>
      before.EffectiveOffset != after.EffectiveOffset || before.First != after.First ||
      before.End != after.End || before.Floor != after.Floor;

    internal static void EncodeMetadata(IcomScopeWaterfallRow row, Span<int> values)
    {
      values[0] = BitConverter.SingleToInt32Bits((float)row.EffectiveOffset);
      values[1] = BitConverter.SingleToInt32Bits(row.First);
      values[2] = BitConverter.SingleToInt32Bits(row.End);
      values[3] = BitConverter.SingleToInt32Bits(row.Floor);
    }
  }
}
