using System.Runtime.InteropServices;

namespace SkyRoof
{
  // GDI+ DrawImage's software bilinear scaler is prohibitively expensive
  // for a full 4K waterfall. Stretch the opaque top-down DIB directly, using
  // a single HDC acquisition for both ring segments. Native samples remain
  // unchanged; nearest-neighbour expansion does not invent new RF bins.
  internal static class IcomScopeGdiDib
  {
    private const int ColorOnColor = 3;
    private const uint SourceCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
      internal uint Size;
      internal int Width;
      internal int Height;
      internal ushort Planes;
      internal ushort BitCount;
      internal uint Compression;
      internal uint SizeImage;
      internal int XPelsPerMeter;
      internal int YPelsPerMeter;
      internal uint ClrUsed;
      internal uint ClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int StretchDIBits(IntPtr hdc, int destX, int destY, int destWidth, int destHeight,
      int sourceX, int sourceY, int sourceWidth, int sourceHeight, IntPtr bits,
      ref BitmapInfo info, uint usage, uint rasterOperation);

    internal static bool TryDraw(Graphics graphics, IntPtr pixels, Size size,
      (Rectangle Dest, Rectangle Source) top, (Rectangle Dest, Rectangle Source) bottom)
    {
      var info = new BitmapInfo
      {
        Size = 40,
        Width = size.Width,
        Height = -size.Height, // top-down; source y uses the physical ring index
        Planes = 1,
        BitCount = 32
      };
      IntPtr dc = graphics.GetHdc();
      int previousMode = SetStretchBltMode(dc, ColorOnColor);
      try
      {
        return Draw(dc, pixels, ref info, top) && Draw(dc, pixels, ref info, bottom);
      }
      finally
      {
        if (previousMode != 0) SetStretchBltMode(dc, previousMode);
        graphics.ReleaseHdc(dc);
      }
    }

    private static bool Draw(IntPtr dc, IntPtr pixels, ref BitmapInfo info,
      (Rectangle Dest, Rectangle Source) part)
    {
      var (dest, source) = part;
      if (source.Height <= 0 || dest.Height <= 0) return true;
      // Each physical ring segment is itself a complete top-down DIB.
      // This avoids driver-dependent source-Y conventions for cropped DIBs.
      var segment = info;
      segment.Height = -source.Height;
      IntPtr start = IntPtr.Add(pixels, source.Y * info.Width * sizeof(int));
      int lines = StretchDIBits(dc, dest.X, dest.Y, dest.Width, dest.Height,
        source.X, 0, source.Width, source.Height, start, ref segment, 0, SourceCopy);
      return lines != 0 && lines != -1;
    }
  }
}
