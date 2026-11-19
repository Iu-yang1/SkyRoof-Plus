namespace SkyRoof
{
  // Reserve the gap in the parent row, not inside a shortened button.
  // The two children share one content rectangle below the spectrum.
  internal static class IcomScopeStatusBarLayout
  {
    private const int GapAt96Dpi = 4;
    private const int ContentAt96Dpi = 24;

    internal static void Apply(RowStyle row, TableLayoutPanel bar, int dpi)
    {
      int gap = Scale(GapAt96Dpi, dpi);
      int content = Scale(ContentAt96Dpi, dpi);
      bar.SuspendLayout();
      try
      {
        bar.Padding = new Padding(0, gap, 0, 0);
        bar.ColumnStyles[1].Width = Scale(70, dpi);
        row.Height = gap + content;
      }
      finally { bar.ResumeLayout(true); }
    }

    private static int Scale(int value, int dpi) =>
      (int)Math.Round(value * Math.Max(96, dpi) / 96.0, MidpointRounding.AwayFromZero);
  }
}
