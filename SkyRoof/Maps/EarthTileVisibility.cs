using VE3NEA;

namespace SkyRoof
{
  internal sealed record EarthTilePlan(int Level, IReadOnlyList<EarthTileKey> Keys);

  internal static class EarthTileVisibility
  {
    internal static (double U, double V) Coordinates(GeoPoint point)
    {
      double u = (point.Longitude + 180) / 360;
      return (u - Math.Floor(u), Math.Clamp((90 - point.Latitude) / 180, 0, 1));
    }

    internal static EarthTileKey At(GeoPoint point, int level)
    {
      var (u, v) = Coordinates(point);
      int width = 1 << level, height = width / 2;
      return new EarthTileKey(level, (int)(u * width),
        Math.Min(height - 1, (int)(v * height)));
    }

    internal static double MaximumZoom(Size viewport, int level) =>
      Math.Max(1, 256.0 * (1 << Math.Clamp(level, 1, 22)) /
        Math.Max(1, Math.Min(viewport.Width, viewport.Height)));

    // A spherical cap conservatively encloses the rectangular AEQD viewport.
    // Its longitude interval wraps at the antimeridian and covers every
    // longitude if a pole lies inside. Bound work BEFORE enumerating tiles.
    internal static EarthTilePlan Plan(GeoPoint center, Size viewport, double zoom,
      IReadOnlyCollection<int> available, IReadOnlySet<int> labelLevels, int capacity)
    {
      int diameter = Math.Min(viewport.Width, viewport.Height);
      if (diameter <= 0 || zoom <= 0 || available.Count == 0)
        return new EarthTilePlan(1, []);
      int[] levels = available.Order().ToArray();
      int desired = (int)Math.Ceiling(Math.Log2(diameter * zoom / 256.0));
      double radius = Math.Min(Math.PI, Math.PI *
        Math.Sqrt((double)viewport.Width * viewport.Width +
          (double)viewport.Height * viewport.Height) / (diameter * zoom));
      EarthTilePlan plan = new(levels[0], []);
      foreach (int level in levels.Where(l => l <= Math.Max(desired, levels[0])))
      {
        var next = BuildPlan(center, radius, level, levels, labelLevels, capacity);
        if (next == null) break;
        plan = next;
      }
      return plan;
    }

    private static EarthTilePlan? BuildPlan(GeoPoint center, double radius, int level,
      int[] levels, IReadOnlySet<int> labelLevels, int capacity)
    {
      var visible = VisibleAtLevel(center, radius, level, capacity);
      if (visible == null) return null;
      var keys = new List<EarthTileKey>();
      foreach (int parent in levels.Where(l => l <= level))
      {
        var ancestors = visible.Select(k => k.Parent(parent)).Distinct()
          .OrderBy(k => Distance(center, k)).ToArray();
        keys.AddRange(ancestors);
        if (labelLevels.Contains(parent))
          keys.AddRange(ancestors.Select(k => k with { Labels = true }));
        if (keys.Count > capacity) return null;
      }
      return new EarthTilePlan(level, keys);
    }

    private static List<EarthTileKey>? VisibleAtLevel(GeoPoint center,
      double radius, int level, int capacity)
    {
      int width = 1 << level, height = width / 2;
      double lat = center.LatitudeRad;
      double lon = center.LongitudeRad;
      double minLat = Math.Max(-Geo.HalfPi, lat - radius);
      double maxLat = Math.Min(Geo.HalfPi, lat + radius);
      bool allLongitude = minLat <= -Geo.HalfPi || maxLat >= Geo.HalfPi;
      double lonSpan = allLongitude ? Math.PI :
        Math.Asin(Math.Clamp(Math.Sin(radius) / Math.Cos(lat), -1, 1));
      int y0 = Math.Clamp((int)Math.Floor((Geo.HalfPi - maxLat) / Math.PI * height), 0, height - 1);
      int y1 = Math.Clamp((int)Math.Floor((Geo.HalfPi - minLat) / Math.PI * height), 0, height - 1);
      int x0 = allLongitude ? 0 : (int)Math.Floor((lon - lonSpan + Math.PI) / Geo.TwoPi * width);
      int x1 = allLongitude ? width - 1 : (int)Math.Floor((lon + lonSpan + Math.PI) / Geo.TwoPi * width);
      int columns = Math.Min(width, x1 - x0 + 1);
      if ((long)columns * (y1 - y0 + 1) > capacity) return null;
      var keys = new List<EarthTileKey>();
      for (int x = x0; x < x0 + columns; x++)
        for (int y = y0; y <= y1; y++)
          keys.Add(new EarthTileKey(level, (x % width + width) % width, y));
      return keys;
    }

    private static double Distance(GeoPoint center, EarthTileKey key)
    {
      var point = new GeoPoint(90 - 180.0 * (key.Y + 0.5) / key.Height,
        360.0 * (key.X + 0.5) / key.Width - 180);
      return (point - center).DistanceRad;
    }
  }
}
