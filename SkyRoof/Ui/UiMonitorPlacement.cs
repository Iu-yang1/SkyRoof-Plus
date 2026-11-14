using System.Drawing;

namespace SkyRoof
{
  internal static class UiMonitorPlacement
  {
    private static long Area(Rectangle rectangle) =>
      (long)Math.Max(0, rectangle.Width) * Math.Max(0, rectangle.Height);

    private static long Overlap(Rectangle first, Rectangle second) =>
      Area(Rectangle.Intersect(first, second));

    // Screen coordinate units are desktop pixels, not widget-local DPI units.
    // Preserve valid spanning/negative-coordinate arrangements; recover only
    // when most of the saved window is no longer on a connected monitor.
    internal static Rectangle KeepWindowVisible(
      Rectangle bounds, IReadOnlyList<Rectangle> workingAreas)
    {
      if (workingAreas.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        return bounds;

      long visible = workingAreas.Sum(area => Overlap(bounds, area));
      if (visible * 2 >= Area(bounds))
        return bounds;

      var target = workingAreas
        .OrderByDescending(area => Overlap(bounds, area))
        .ThenBy(area =>
        {
          long dx = (long)(bounds.Left + bounds.Width / 2) -
            (area.Left + area.Width / 2);
          long dy = (long)(bounds.Top + bounds.Height / 2) -
            (area.Top + area.Height / 2);
          return dx * dx + dy * dy;
        })
        .First();
      int width = Math.Min(bounds.Width, target.Width);
      int height = Math.Min(bounds.Height, target.Height);
      return new Rectangle(
        Math.Clamp(bounds.Left, target.Left, target.Right - width),
        Math.Clamp(bounds.Top, target.Top, target.Bottom - height),
        width, height);
    }
  }
}
