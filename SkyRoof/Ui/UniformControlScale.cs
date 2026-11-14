using System.Drawing;
using System.Windows.Forms;

namespace SkyRoof
{
  // Snapshots the finished designer/WinForms DPI baseline exactly once.
  // Each scale is calculated against the SAME baseline, not the last scale.
  // This keeps font metrics, positions and hit targets in one coordinate system.
  internal sealed class UniformControlScale : IDisposable
  {
    private sealed record Snapshot(Control Control, Rectangle Bounds,
      Padding Padding, Padding Margin, Size Minimum, Size Maximum, Font Font);
    private readonly Snapshot[] snapshots;
    private readonly Dictionary<string, Font> fonts = new();
    private float current = 1f;

    internal UniformControlScale(Control root)
    {
      var controls = new List<Control>();
      static void Visit(Control control, List<Control> list)
      {
        list.Add(control);
        foreach (Control child in control.Controls)
          Visit(child, list);
      }
      Visit(root, controls);
      snapshots = controls.Select(c => new Snapshot(
        c, c.Bounds, c.Padding, c.Margin, c.MinimumSize, c.MaximumSize, c.Font)).ToArray();
    }

    internal float Current => current;

    internal int RequiredWidth(params Control[] excluded) =>
      snapshots.Skip(1)
        .Where(s => s.Control.Parent == snapshots[0].Control &&
          !excluded.Contains(s.Control))
        .Sum(s => s.Bounds.Width);

    internal static int Pixel(int value, float scale) =>
      (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    internal static Rectangle ScaleBounds(Rectangle bounds, float scale) =>
      new(Pixel(bounds.X, scale), Pixel(bounds.Y, scale),
        Pixel(bounds.Width, scale), Pixel(bounds.Height, scale));

    private static Padding ScalePadding(Padding padding, float scale) =>
      new(Pixel(padding.Left, scale), Pixel(padding.Top, scale),
        Pixel(padding.Right, scale), Pixel(padding.Bottom, scale));

    private Font ScaledFont(Font original, float scale)
    {
      if (Math.Abs(scale - 1f) < .001f) return original;
      float size = Math.Max(5f, original.SizeInPoints * scale);
      string key = $"{original.Name}|{(int)original.Style}|{size:F3}";
      if (!fonts.TryGetValue(key, out Font? font))
      {
        font = new Font(original.FontFamily, size, original.Style,
          GraphicsUnit.Point, original.GdiCharSet, original.GdiVerticalFont);
        fonts.Add(key, font);
      }
      return font;
    }

    internal void Apply(float scale)
    {
      scale = Math.Clamp(scale, .25f, 1.5f);
      if (Math.Abs(scale - current) < .005f) return;
      foreach (var item in snapshots) item.Control.SuspendLayout();
      try
      {
        foreach (var item in snapshots)
        {
          var control = item.Control;
          var bounds = ScaleBounds(item.Bounds, scale);
          control.Margin = ScalePadding(item.Margin, scale);
          control.Padding = ScalePadding(item.Padding, scale);
          control.MinimumSize = new Size(Pixel(item.Minimum.Width, scale),
            Pixel(item.Minimum.Height, scale));
          if (item.Maximum.Width > 0 || item.Maximum.Height > 0)
            control.MaximumSize = new Size(
              Pixel(item.Maximum.Width, scale), Pixel(item.Maximum.Height, scale));
          switch (control.Dock)
          {
            case DockStyle.Top:
            case DockStyle.Bottom:
              control.Height = bounds.Height;
              break;
            case DockStyle.Left:
            case DockStyle.Right:
              control.Width = bounds.Width;
              break;
            case DockStyle.Fill:
              break;
            default:
              control.Bounds = bounds;
              break;
          }
          control.Font = ScaledFont(item.Font, scale);
        }
        current = scale;
      }
      finally
      {
        for (int i = snapshots.Length - 1; i >= 0; i--)
          snapshots[i].Control.ResumeLayout(false);
      }
      snapshots[0].Control.PerformLayout();
    }

    public void Dispose()
    {
      foreach (var font in fonts.Values) font.Dispose();
      fonts.Clear();
    }
  }
}
