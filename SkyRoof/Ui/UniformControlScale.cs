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
    private readonly bool scaleRootBounds;
    private readonly Dictionary<string, Font> fonts = new();
    private float current = 1f;

    internal UniformControlScale(Control root, bool scaleRootBounds = true)
    {
      this.scaleRootBounds = scaleRootBounds;
      var controls = new List<Control>();
      static void Visit(Control control, List<Control> list)
      {
        list.Add(control);
        foreach (Control child in control.Controls)
          Visit(child, list);
      }
      Visit(root, controls);
      // WinForms has already applied system DPI to the designer baseline.
      // Explicitly scaling fonts under a UserControl.AutoScaleMode.Font would
      // trigger a second automatic scale of its children.
      foreach (var c in controls.OfType<ContainerControl>())
        c.AutoScaleMode = AutoScaleMode.None;
      snapshots = controls.Select(c => new Snapshot(
        c, c.Bounds, c.Padding, c.Margin, c.MinimumSize, c.MaximumSize, c.Font)).ToArray();
    }

    internal float Current => current;
    internal Size OriginalMinimumSize => snapshots[0].Minimum;

    internal int RequiredWidth(params Control[] excluded) =>
      snapshots.Skip(1)
        .Where(s => s.Control.Parent == snapshots[0].Control &&
          !excluded.Contains(s.Control))
        .Sum(s => s.Bounds.Width);

    internal static (float Factor, bool ShowPhoto) FitToolbar(
      int availableWidth, int fullWidth, int essentialWidth, float preferred)
    {
      int available = Math.Max(1, availableWidth - 12);
      float withPhoto = Math.Min(preferred, available / (float)fullWidth);
      bool showPhoto = withPhoto >= .72f;
      float factor = showPhoto ? withPhoto :
        Math.Min(preferred, available / (float)essentialWidth);
      return (Math.Clamp(factor, .25f, 1.5f), showPhoto);
    }

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
          if (!scaleRootBounds && ReferenceEquals(item, snapshots[0]))
          {
            item.Control.Font = ScaledFont(item.Font, scale);
            continue;
          }
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
