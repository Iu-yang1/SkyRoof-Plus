namespace SkyRoof.RemoteDesktop;

// Horizon is the intersection of the WGS-84 ellipsoid and the satellite's tangent cone.
// Scaling ECEF to a unit sphere turns the tangent boundary into a plane circle.
internal static class EarthHorizon
{
    private const double A = 6378137.0;
    private const double B = 6356752.314245;
    private const double E2 = 1 - B * B / (A * A);
    private const double Rad = Math.PI / 180;

    internal static IReadOnlyList<(double Lat, double Lon)> Outline(double latitude, double longitude, double height)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || !double.IsFinite(height) ||
            Math.Abs(latitude) > 90 || height <= 0) return [];
        double lat = latitude * Rad, lon = longitude * Rad;
        double sin = Math.Sin(lat), cos = Math.Cos(lat);
        double n = A / Math.Sqrt(1 - E2 * sin * sin);
        double xy = (n + height) * cos / A;
        double z = (n * (1 - E2) + height) * sin / B;
        double distance = Math.Sqrt(xy * xy + z * z);
        if (distance <= 1) return [];
        double ux = xy / distance, uz = z / distance;
        double center = 1 / distance;
        double radius = Math.Sqrt(1 - center * center);
        var points = new List<(double Lat, double Lon)>(181);
        for (int i = 0; i <= 180; i++)
        {
            double angle = i * 2 * Math.PI / 180;
            double east = radius * Math.Cos(angle);
            double north = radius * Math.Sin(angle);
            double horizontal = center * ux - north * uz;
            double vertical = center * uz + north * ux;
            double x = A * (horizontal * Math.Cos(lon) - east * Math.Sin(lon));
            double y = A * (horizontal * Math.Sin(lon) + east * Math.Cos(lon));
            double surfaceZ = B * vertical;
            points.Add((Math.Atan2(surfaceZ / (B * B), Math.Sqrt(x * x + y * y) / (A * A)) / Rad,
                Math.Atan2(y, x) / Rad));
        }
        return points;
    }
}
