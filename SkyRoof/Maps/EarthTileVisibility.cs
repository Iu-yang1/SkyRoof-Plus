using VE3NEA;

namespace SkyRoof
{
  internal sealed record EarthTilePlan(int Level, IReadOnlyList<EarthTileKey> Keys);

  internal sealed record EarthTilePlanOptions(IReadOnlyCollection<int> Levels,
    IReadOnlySet<int> Labels, int Capacity, EarthTileProjection Projection);

  internal static class EarthTileVisibility
  {
    internal static (double U, double V) Coordinates(GeoPoint point)
    {
      return EarthTileGrid.Coordinates(point, EarthTileProjection.Geographic);
    }

    internal static EarthTileKey At(GeoPoint point, int level,
      EarthTileProjection projection = EarthTileProjection.Geographic)
    {
      var (u, v) = EarthTileGrid.Coordinates(point, projection);
      int width = 1 << level, height = EarthTileGrid.Height(level, projection);
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
      IReadOnlyCollection<int> available, IReadOnlySet<int> labelLevels, int capacity) =>
      Plan(center, viewport, zoom, new EarthTilePlanOptions(available, labelLevels,
        capacity, EarthTileProjection.Geographic));

    internal static EarthTilePlan Plan(GeoPoint center, Size viewport, double zoom, EarthTilePlanOptions options)
    {
      int diameter = Math.Min(viewport.Width, viewport.Height);
      if (diameter <= 0 || zoom <= 0 || options.Levels.Count == 0)
        return new EarthTilePlan(1, []);
      int[] levels = options.Levels.Order().ToArray();
      int desired = (int)Math.Ceiling(Math.Log2(diameter * zoom / 256.0));
      double radius = Math.Min(Math.PI, Math.PI *
        Math.Sqrt((double)viewport.Width * viewport.Width +
          (double)viewport.Height * viewport.Height) / (diameter * zoom));
      (center, radius) = EarthTileGrid.PlanningCap(center, radius, options.Projection);
      EarthTilePlan plan = new(levels[0], []);
      foreach (int level in levels.Where(l => l <= Math.Max(desired, levels[0])))
      {
        var next = BuildPlan(center, radius, level, levels, options);
        if (next == null) break;
        plan = next;
      }
      return plan;
    }

    private static EarthTilePlan? BuildPlan(GeoPoint center, double radius, int level,
      int[] levels, EarthTilePlanOptions options)
    {
      var visible = VisibleAtLevel(center, radius, level, options);
      if (visible == null) return null;
      var keys = new List<EarthTileKey>();
      foreach (int parent in levels.Where(l => l <= level))
      {
        var ancestors = visible.Select(k => k.Parent(parent)).Distinct()
          .OrderBy(k => Distance(center, k, options.Projection)).ToArray();
        keys.AddRange(ancestors);
        if (options.Labels.Contains(parent))
          keys.AddRange(ancestors.Select(k => k with { Labels = true }));
        if (keys.Count > options.Capacity) return null;
      }
      return new EarthTilePlan(level, keys);
    }

    private static List<EarthTileKey>? VisibleAtLevel(GeoPoint center,
      double radius, int level, EarthTilePlanOptions options)
    {
      int width = 1 << level, height = EarthTileGrid.Height(level, options.Projection);
      double lat = center.LatitudeRad;
      double lon = center.LongitudeRad;
      double minLat = Math.Max(-Geo.HalfPi, lat - radius);
      double maxLat = Math.Min(Geo.HalfPi, lat + radius);
      bool allLongitude = minLat <= -Geo.HalfPi || maxLat >= Geo.HalfPi;
      double lonSpan = allLongitude ? Math.PI :
        Math.Asin(Math.Clamp(Math.Sin(radius) / Math.Cos(lat), -1, 1));
      int y0 = Math.Clamp((int)Math.Floor(EarthTileGrid.LatitudeV(maxLat * Geo.DinR, options.Projection) * height), 0, height - 1);
      int y1 = Math.Clamp((int)Math.Floor(EarthTileGrid.LatitudeV(minLat * Geo.DinR, options.Projection) * height), 0, height - 1);
      int x0 = allLongitude ? 0 : (int)Math.Floor((lon - lonSpan + Math.PI) / Geo.TwoPi * width);
      int x1 = allLongitude ? width - 1 : (int)Math.Floor((lon + lonSpan + Math.PI) / Geo.TwoPi * width);
      int columns = Math.Min(width, x1 - x0 + 1);
      if ((long)columns * (y1 - y0 + 1) > options.Capacity) return null;
      var keys = new List<EarthTileKey>();
      for (int x = x0; x < x0 + columns; x++)
        for (int y = y0; y <= y1; y++)
          keys.Add(new EarthTileKey(level, (x % width + width) % width, y));
      return keys;
    }

    private static double Distance(GeoPoint center, EarthTileKey key, EarthTileProjection projection)
    {
      var point = new GeoPoint(EarthTileGrid.LatitudeAt((key.Y + 0.5) /
        EarthTileGrid.Height(key.Level, projection), projection),
        360.0 * (key.X + 0.5) / key.Width - 180);
      return (point - center).DistanceRad;
    }
  }
}
