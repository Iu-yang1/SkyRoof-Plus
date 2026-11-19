namespace SkyRoof
{
  // HWND clipping prevents the parent buffer blit from touching the child;
  // GDI+ clipping independently prevents writes inside its staging buffer.
  internal static class IcomScopePaintOwnership
  {
    internal const int ClipChildren = 0x02000000; // WS_CLIPCHILDREN
    internal const int ClipSiblings = 0x04000000; // WS_CLIPSIBLINGS
    internal const int EraseBackground = 0x0014; // WM_ERASEBKGND

    internal static Region CreateChromeRegion(Rectangle client, Point origin,
      Rectangle plot, Rectangle waterfall)
    {
      var result = new Region(client);
      Exclude(result, origin, plot);
      Exclude(result, origin, waterfall);
      return result;
    }

    private static void Exclude(Region region, Point origin, Rectangle owned)
    {
      if (owned.Width <= 0 || owned.Height <= 0) return;
      owned.Offset(origin);
      region.Exclude(owned);
    }
  }
}
