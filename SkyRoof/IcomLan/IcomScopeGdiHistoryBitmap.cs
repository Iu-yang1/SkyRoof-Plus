using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
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

    internal void Draw(Graphics graphics, Rectangle destination, int head, double start, double end)
    {
      int first = Math.Clamp((int)Math.Floor(start * (Points - 1)), 0, Points - 1);
      int right = Math.Clamp((int)Math.Ceiling(end * (Points - 1)) + 1, first + 1, Points);
      int width = right - first;
      int rows = Image.Height;
      head = Math.Max(0, head);
      int firstRows = rows - head;
      int split = (int)Math.Round(destination.Height * firstRows / (double)rows);
      GraphicsState state = graphics.Save();
      try
      {
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        DrawPart(graphics, new Rectangle(destination.X, destination.Y, destination.Width, split),
          new Rectangle(first, head, width, firstRows));
        DrawPart(graphics, new Rectangle(destination.X, destination.Y + split, destination.Width, destination.Height - split),
          new Rectangle(first, 0, width, head));
      }
      finally { graphics.Restore(state); }
    }

    private void DrawPart(Graphics graphics, Rectangle dest, Rectangle source)
    {
      if (source.Height <= 0 || dest.Height <= 0) return;
      graphics.DrawImage(Image, dest, source, GraphicsUnit.Pixel);
    }

    public void Dispose()
    {
      if (!pin.IsAllocated) return;
      Image.Dispose();
      pin.Free();
    }
  }
}
