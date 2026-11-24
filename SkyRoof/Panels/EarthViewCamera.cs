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
      Center = OffsetFromCenter(east / length, north / length, distance);
      IsManual = true;
      return true;
    }

    // Use the Earth-fixed tangent basis shared by the GPU renderer. The
    // longitude remains well-conditioned even when the centre is a pole.
    private GeoPoint OffsetFromCenter(double east, double north, double distance)
    {
      double lat = Center.LatitudeRad, lon = Center.LongitudeRad;
      double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
      double sinLon = Math.Sin(lon), cosLon = Math.Cos(lon);
      double a = Math.Cos(distance), b = Math.Sin(distance);
      double x = a * cosLat * cosLon + b * (-east * sinLon - north * sinLat * cosLon);
      double y = a * cosLat * sinLon + b * (east * cosLon - north * sinLat * sinLon);
      double z = a * sinLat + b * north * cosLat;
      return new GeoPoint(
        Math.Atan2(z, Math.Sqrt(x * x + y * y)) * Geo.DinR,
        Math.Atan2(y, x) * Geo.DinR);
    }

    internal PointF Project(GeoPoint point, Size viewport, double zoom)
    {
      if (viewport.Width <= 0 || viewport.Height <= 0) return PointF.Empty;
      double deltaLon = point.LongitudeRad - Center.LongitudeRad;
      double sinHalf = Math.Sin(deltaLon / 2);
      double sinCenter = Math.Sin(Center.LatitudeRad);
      double cosPoint = Math.Cos(point.LatitudeRad);
      double east = cosPoint * Math.Sin(deltaLon);
      double north = Math.Sin(point.LatitudeRad - Center.LatitudeRad) +
        2 * sinCenter * cosPoint * sinHalf * sinHalf;
      double tangent = Math.Sqrt(east * east + north * north);
      double dot = sinCenter * Math.Sin(point.LatitudeRad) +
        Math.Cos(Center.LatitudeRad) * cosPoint * Math.Cos(deltaLon);
      double angular = Math.Atan2(tangent, dot);
      if (tangent == 0) return PointF.Empty;
      double scale = angular / tangent * zoom * Math.Min(viewport.Width, viewport.Height) / Math.PI;
      return new PointF((float)(east * scale / viewport.Width),
        (float)(north * scale / viewport.Height));
    }

    // Inverse of Project, matching the renderer's AEQD disk and aspect ratio.
    // Points outside the Earth disk and its singular antipode have no unique target.
    internal bool TryUnproject(PointF pixel, Size viewport, double zoom, out GeoPoint point)
    {
      point = Center;
      double diameter = Math.Min(viewport.Width, viewport.Height);
      if (diameter <= 0 || !double.IsFinite(zoom) || zoom <= 0) return false;
      double east = (2d * pixel.X - viewport.Width) / diameter;
      double north = (viewport.Height - 2d * pixel.Y) / diameter;
      double length = Math.Sqrt(east * east + north * north);
      double radius = length * Math.PI / zoom;
      if (!double.IsFinite(radius) || radius >= Math.PI) return false;
      if (length == 0) return true;
      point = OffsetFromCenter(east / length, north / length, radius);
      return true;
    }

    internal static bool TryGroundAzimuth(GeoPoint station, GeoPoint target, out double azimuth)
    {
      double delta = target.LongitudeRad - station.LongitudeRad;
      double east = Math.Cos(target.LatitudeRad) * Math.Sin(delta);
      double half = Math.Sin(delta / 2);
      double north = Math.Sin(target.LatitudeRad - station.LatitudeRad) +
        2 * Math.Sin(station.LatitudeRad) * Math.Cos(target.LatitudeRad) * half * half;
      azimuth = 0;
      // Coincident and antipodal points have no unique initial great-circle bearing.
      if (!double.IsFinite(east) || !double.IsFinite(north) ||
          east * east + north * north < 1e-24) return false;
      azimuth = (Math.Atan2(east, north) * Geo.DinR + 360) % 360;
      return true;
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
