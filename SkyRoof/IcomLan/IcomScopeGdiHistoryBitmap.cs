using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SkyRoof
{
  // GDI+ reads this persistent, pinned pixel buffer on the UI thread.
  // No per-frame LockBits staging or full-bitmap copy is needed.
  internal sealed class IcomScopeGdiHistoryBitmap : IDisposable
  {
    private const int Points = IcomScopeWaterfallRing.Points;
    private readonly int[] pixels;
    private GCHandle pin;
    private int[][]? uploaded;
    internal Bitmap Image { get; }

    internal IcomScopeGdiHistoryBitmap(int rows)
    {
      pixels = new int[Points * rows];
      pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
      try
      {
        Image = new Bitmap(Points, rows, Points * sizeof(int),
          PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject());
      }
      catch
      {
        pin.Free();
        throw;
      }
    }

    internal int Update(int[][] rows)
    {
      if (ReferenceEquals(rows, uploaded)) return 0;
      int copied = 0;
      for (int y = 0; y < rows.Length; y++)
      {
        if (uploaded != null && ReferenceEquals(rows[y], uploaded[y])) continue;
        rows[y].AsSpan().CopyTo(pixels.AsSpan(y * Points, Points));
        copied++;
      }
      uploaded = rows;
      return copied;
    }

    public void Dispose()
    {
      if (!pin.IsAllocated) return;
      Image.Dispose();
      pin.Free();
    }
  }
}
