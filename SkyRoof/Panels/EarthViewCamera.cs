using VE3NEA;

namespace SkyRoof
{
  // The azimuthal-equidistant projection is north-up at its geographical
  // centre. Tracking updates may move the centre only in follow mode.
  internal sealed class EarthViewCamera
  {
    internal GeoPoint Center { get; private set; }
    internal bool IsManual { get; private set; }

    internal void Follow(GeoPoint target)
    {
      if (!IsManual) Center = target;
    }

    internal void Reset(GeoPoint target)
    {
      IsManual = false;
      Center = target;
    }

    // Translate the camera along the local tangent plane. The inverse
    // spherical exponential map handles the antimeridian and both poles.
    // A horizontal/vertical drag has the same angular scale at any aspect.
    internal bool Pan(int dx, int dy, Size viewport, double zoom)
    {
      int diameter = Math.Min(viewport.Width, viewport.Height);
      if (diameter <= 0 || zoom <= 0 || (dx == 0 && dy == 0)) return false;

      double east = -(double)dx, north = dy;
      double length = Math.Sqrt(east * east + north * north);
      double distance = length * Geo.TwoPi / (diameter * zoom);
      double lat = Center.LatitudeRad, lon = Center.LongitudeRad;
      double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
      double sinLon = Math.Sin(lon), cosLon = Math.Cos(lon);
      double a = Math.Cos(distance), b = Math.Sin(distance) / length;
      double x = a * cosLat * cosLon + b * (-east * sinLon - north * sinLat * cosLon);
      double y = a * cosLat * sinLon + b * (east * cosLon - north * sinLat * sinLon);
      double z = a * sinLat + b * north * cosLat;
      Center = new GeoPoint(
        Math.Atan2(z, Math.Sqrt(x * x + y * y)) * Geo.DinR,
        Math.Atan2(y, x) * Geo.DinR);
      IsManual = true;
      return true;
    }

    internal PointF Project(GeoPoint point, Size viewport, double zoom)
    {
      if (viewport.Width <= 0 || viewport.Height <= 0) return PointF.Empty;
      var path = point - Center;
      double radius = path.DistanceRad / Math.PI * zoom;
      double diameter = Math.Min(viewport.Width, viewport.Height);
      return new PointF(
        (float)(radius * Math.Sin(path.AzimuthRad) * diameter / viewport.Width),
        (float)(radius * Math.Cos(path.AzimuthRad) * diameter / viewport.Height));
    }

    internal float Heading(GeoPoint point, GeoPoint next, Size viewport, double zoom)
    {
      PointF a = Project(point, viewport, zoom);
      PointF b = Project(next, viewport, zoom);
      double dx = (b.X - a.X) * viewport.Width;
      double dy = (b.Y - a.Y) * viewport.Height;
      return (float)(Math.Atan2(dy, dx) - Math.PI / 4);
    }
  }
}
