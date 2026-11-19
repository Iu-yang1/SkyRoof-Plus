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
    private IcomScopeWaterfallTimeRaster? timeRaster;
    private IcomScopeGdiHistoryBitmap? compact;
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
      if (destination.Height > 0 && destination.Height < Image.Height && uploaded != null)
      {
        DrawCompact(graphics, destination, head, start, end);
        return;
      }
      int first = Math.Clamp((int)Math.Floor(start * (Points - 1)), 0, Points - 1);
      int right = Math.Clamp((int)Math.Ceiling(end * (Points - 1)) + 1, first + 1, Points);
      int width = right - first;
      int rows = Image.Height;
      head = Math.Max(0, head);
      int split = (int)Math.Round(destination.Height * (rows - head) / (double)rows);
      var top = new Rectangle(destination.X, destination.Y, destination.Width, split);
      var bottom = new Rectangle(destination.X, destination.Y + split, destination.Width, destination.Height - split);
      var topSource = new Rectangle(first, head, width, rows - head);
      var bottomSource = new Rectangle(first, 0, width, head);
      if (IcomScopeGdiDib.TryDraw(graphics, pin.AddrOfPinnedObject(), Image.Size,
          (top, topSource), (bottom, bottomSource))) return;
      DrawCompatibility(graphics, (top, topSource), (bottom, bottomSource));
    }

    private void DrawCompact(Graphics graphics, Rectangle destination, int head, double start, double end)
    {
      if (uploaded == null) return;
      if (compact == null || timeRaster == null || compact.Image.Height != destination.Height)
      {
        compact?.Dispose();
        compact = new IcomScopeGdiHistoryBitmap(destination.Height);
        timeRaster = new IcomScopeWaterfallTimeRaster(destination.Height);
      }
      var mapped = timeRaster.Update(uploaded, head);
      compact.Update(mapped.Rows);
      compact.Draw(graphics, destination, mapped.Head, start, end);
    }

    private void DrawCompatibility(Graphics graphics, (Rectangle Dest, Rectangle Source) top,
      (Rectangle Dest, Rectangle Source) bottom)
    {
      GraphicsState state = graphics.Save();
      try
      {
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        DrawPart(graphics, top);
        DrawPart(graphics, bottom);
      }
      finally { graphics.Restore(state); }
    }

    private void DrawPart(Graphics graphics, (Rectangle Dest, Rectangle Source) part)
    {
      if (part.Source.Height <= 0 || part.Dest.Height <= 0) return;
      graphics.DrawImage(Image, part.Dest, part.Source, GraphicsUnit.Pixel);
    }

    public void Dispose()
    {
      if (!pin.IsAllocated) return;
      compact?.Dispose();
      Image.Dispose();
      pin.Free();
    }
  }
}
