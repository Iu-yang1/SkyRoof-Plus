using VE3NEA;

namespace SkyRoof
{
  internal enum EarthTileProjection { Geographic, Mercator, GcjMercator }

  // The orbital engine and the AEQD camera remain WGS-84. Only source tile
  // coordinates are transformed; the GCJ approximation is not a legal border mask.
  internal static class EarthTileGrid
  {
    internal const double MercatorLimit = 85.0511287798066;
    internal static int Height(int level, EarthTileProjection projection) =>
      projection == EarthTileProjection.Geographic ? (1 << level) / 2 : 1 << level;

    internal static (double U, double V) Coordinates(GeoPoint point, EarthTileProjection projection)
    {
      point = SourcePoint(point, projection);
      double u = (point.Longitude + 180) / 360;
      return (u - Math.Floor(u), LatitudeV(point.Latitude, projection));
    }

    internal static GeoPoint SourcePoint(GeoPoint point, EarthTileProjection projection) =>
      projection == EarthTileProjection.GcjMercator ? Gcj02.Forward(point) : point;

    internal static double LatitudeV(double latitude, EarthTileProjection projection)
    {
      if (projection == EarthTileProjection.Geographic)
        return Math.Clamp((90 - latitude) / 180, 0, 1);
      double phi = Math.Clamp(latitude, -MercatorLimit, MercatorLimit) * Geo.RinD;
      return Math.Clamp(0.5 - Math.Asinh(Math.Tan(phi)) / Geo.TwoPi, 0, 1);
    }

    internal static double LatitudeAt(double v, EarthTileProjection projection) =>
      projection == EarthTileProjection.Geographic ? 90 - 180 * v :
        Math.Atan(Math.Sinh(Math.PI * (1 - 2 * v))) * Geo.DinR;

    internal static (GeoPoint Center, double Radius) PlanningCap(
      GeoPoint center, double radius, EarthTileProjection projection)
    {
      if (projection != EarthTileProjection.GcjMercator) return (center, radius);
      // Pad any cap crossing the conventional GCJ applicability rectangle
      // for the discontinuity at its boundary. Interior caps use a local
      // expansion instead, so deep zoom does not request a fixed large area.
      double extent = radius * Geo.DinR;
      double cos = Math.Cos(Math.Min(90, Math.Abs(center.Latitude) + extent) * Geo.RinD);
      double lonExtent = extent / Math.Max(0.01, cos);
      if (extent >= 10 ||
          Gcj02.CrossesBoundary(center, extent, lonExtent))
        return (center, Math.Min(Math.PI, radius + 0.03 * Geo.RinD));
      if (!Gcj02.Applies(center)) return (center, radius);
      return (Gcj02.Forward(center), Math.Min(Math.PI, radius * 1.15 + 2e-7));
    }
  }

  internal static class Gcj02
  {
    private const double A = 6378245.0;
    private const double Ee = 0.006693421622965943;
    private const double West = 72.004, East = 137.8347, South = 0.8293, North = 55.8271;

    internal static bool Applies(GeoPoint p) =>
      p.Longitude >= West && p.Longitude <= East && p.Latitude >= South && p.Latitude <= North;

    internal static bool CrossesBoundary(GeoPoint p, double latExtent, double lonExtent) =>
      Math.Abs(p.Latitude - South) <= latExtent || Math.Abs(p.Latitude - North) <= latExtent ||
      Math.Abs(p.Longitude - West) <= lonExtent || Math.Abs(p.Longitude - East) <= lonExtent;

    internal static GeoPoint Forward(GeoPoint p)
    {
      if (!Applies(p)) return p;
      double x = p.Longitude - 105, y = p.Latitude - 35;
      double phi = p.LatitudeRad, sin = Math.Sin(phi);
      double magic = 1 - Ee * sin * sin, root = Math.Sqrt(magic);
      double dlat = LatitudeOffset(x, y) * Geo.DinR / (A * (1 - Ee) / (magic * root));
      double dlon = LongitudeOffset(x, y) * Geo.DinR / (A / root * Math.Cos(phi));
      return new GeoPoint(p.Latitude + dlat, p.Longitude + dlon);
    }

    private static double Shared(double x) =>
      (20 * Math.Sin(6 * x * Math.PI) + 20 * Math.Sin(2 * x * Math.PI)) * 2 / 3;

    private static double LatitudeOffset(double x, double y) =>
      -100 + 2*x + 3*y + .2*y*y + .1*x*y + .2*Math.Sqrt(Math.Abs(x)) + Shared(x) +
      (20*Math.Sin(y*Math.PI) + 40*Math.Sin(y/3*Math.PI))*2/3 +
      (160*Math.Sin(y/12*Math.PI) + 320*Math.Sin(y*Math.PI/30))*2/3;

    private static double LongitudeOffset(double x, double y) =>
      300 + x + 2*y + .1*x*x + .1*x*y + .1*Math.Sqrt(Math.Abs(x)) + Shared(x) +
      (20*Math.Sin(x*Math.PI) + 40*Math.Sin(x/3*Math.PI))*2/3 +
      (150*Math.Sin(x/12*Math.PI) + 300*Math.Sin(x/30*Math.PI))*2/3;
  }
}
