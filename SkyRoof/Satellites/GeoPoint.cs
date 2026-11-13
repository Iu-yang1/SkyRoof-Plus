namespace VE3NEA
{
  public static class Geo
  {
    public const double DinR = 180 / Math.PI;
    public const double RinD = 1 / DinR;
    public const double EarthRadiusKm = 6378; // also km per radian
    public const double RinKm = 1 / EarthRadiusKm;
    public const double KmInR = 1 / RinKm;
    public const double HalfPi = Math.PI / 2;
    public const double TwoPi = 2 * Math.PI;
    public const double EquatorLengthKm = TwoPi * EarthRadiusKm;
  }

  public class GridSquare
  {
    // Maidenhead subdivisions alternate decimal and 24-letter pairs.
    // Every returned position is the CENTER of the complete locator cell.
    public static GeoPoint ToGeoPoint(string square)
    {
      if (!IsValid(square))
        throw new ArgumentException($"Invalid grid square: '{square}'", nameof(square));

      square = square.ToUpperInvariant();
      double longitude = -180, latitude = -90;
      double longitudeCell = 20, latitudeCell = 10;

      for (int i = 0; i < square.Length; i += 2)
      {
        if (i != 0)
        {
          int radix = i % 4 == 2 ? 10 : 24;
          longitudeCell /= radix;
          latitudeCell /= radix;
        }

        int first = square[i] - (i % 4 == 2 ? '0' : 'A');
        int second = square[i + 1] - (i % 4 == 2 ? '0' : 'A');
        longitude += first * longitudeCell;
        latitude += second * latitudeCell;
      }

      return new GeoPoint(latitude + latitudeCell / 2, longitude + longitudeCell / 2);
    }

    public static string FromGeoPoint(GeoPoint point, int length)
    {
      if (length is not (2 or 4 or 6 or 8 or 10))
        throw new ArgumentOutOfRangeException(nameof(length));

      double longitude = Math.Clamp(point.Longitude, -180d, Math.BitDecrement(180d)) + 180;
      double latitude = Math.Clamp(point.Latitude, -90d, Math.BitDecrement(90d)) + 90;
      double longitudeCell = 20, latitudeCell = 10;
      var result = new System.Text.StringBuilder(length);

      for (int i = 0; i < length; i += 2)
      {
        int radix = i == 0 ? 18 : i % 4 == 2 ? 10 : 24;
        if (i != 0)
        {
          longitudeCell /= radix;
          latitudeCell /= radix;
        }

        int x = Math.Clamp((int)Math.Floor(longitude / longitudeCell), 0, radix - 1);
        int y = Math.Clamp((int)Math.Floor(latitude / latitudeCell), 0, radix - 1);
        char start = i % 4 == 2 ? '0' : i == 0 ? 'A' : 'a';
        result.Append((char)(start + x));
        result.Append((char)(start + y));
        longitude -= x * longitudeCell;
        latitude -= y * latitudeCell;
      }

      return result.ToString();
    }

    public static bool IsValid(string? square)
    {
      if (square == null || square.Length is not (2 or 4 or 6 or 8 or 10))
        return false;
      if (square.Equals("RR73", StringComparison.OrdinalIgnoreCase))
        return false;

      for (int i = 0; i < square.Length; i += 2)
      {
        char first = char.ToUpperInvariant(square[i]);
        char second = char.ToUpperInvariant(square[i + 1]);
        char max = i == 0 ? 'R' : 'X';
        if (i % 4 == 2)
        {
          if (first < '0' || first > '9' || second < '0' || second > '9')
            return false;
        }
        else if (first < 'A' || first > max || second < 'A' || second > max)
          return false;
      }
      return true;
    }

    internal static string? ForceCase(string? square)
    {
      if (square == null || !IsValid(square))
        return square;

      string upper = square.ToUpperInvariant();
      return square.Length switch
      {
        6 => upper[..4] + upper[4..].ToLowerInvariant(),
        8 => upper[..4] + upper[4..6].ToLowerInvariant() + upper[6..],
        10 => upper[..4] + upper[4..6].ToLowerInvariant() +
          upper[6..8] + upper[8..].ToLowerInvariant(),
        _ => upper
      };
    }
  }

  public struct GeoPath
  {
    public double AzimuthRad, DistanceRad;


    public GeoPath(double azimuth, double distance) { AzimuthRad = DistanceRad = 0; Azimuth = azimuth; Distance = distance; }
    
    public double Azimuth { get => AzimuthRad * Geo.DinR; set => AzimuthRad = value * Geo.RinD; }
    public double Distance { get => DistanceRad * Geo.KmInR; set => DistanceRad = value * Geo.RinKm; }

    public double LongPathAzimuth { get => 360 - Azimuth; }
    public double LongPathDistance { get => Geo.EquatorLengthKm - Distance; set => DistanceRad = value * Geo.RinKm; }

    public override string ToString()
    {
      return $"{Azimuth:F0}º  {Distance:F0} km  (LP {LongPathAzimuth:F0}º  {LongPathDistance:F0} km)";
    }
  }

  public struct GeoPoint
  {
    public static GeoPoint NorthPole = new(90, 0);
    public static GeoPoint SouthPole = new(-90, 0);

    internal double LatitudeRad, LongitudeRad;

    public double Latitude { get => LatitudeRad * Geo.DinR; set => LatitudeRad = value * Geo.RinD; }
    public double Longitude { get => LongitudeRad * Geo.DinR; set => LongitudeRad = value * Geo.RinD; }


    public GeoPoint(double latitude, double longitude)
    {
      LatitudeRad = latitude * Geo.RinD;
      LongitudeRad = longitude * Geo.RinD;
    }

    public string ToGridSquare(int length)
    {
      return GridSquare.FromGeoPoint(this, length);
    }

    public GeoPoint MoveBy(GeoPath path)
    {
      GeoPoint point = new GeoPoint();

      double a = Math.Sin(LatitudeRad) * Math.Cos(path.DistanceRad)
        + Math.Cos(LatitudeRad) * Math.Sin(path.DistanceRad) * Math.Cos(path.AzimuthRad);
      a = Math.Max(-1, Math.Min(1, a));
      point.LatitudeRad = Math.Asin(a);

      double num = Math.Sin(path.AzimuthRad) * Math.Sin(path.DistanceRad) * Math.Cos(LatitudeRad);
      double den = Math.Cos(path.DistanceRad) - Math.Sin(LatitudeRad) * a;
      point.LongitudeRad = LongitudeRad + Math.Atan2(num, den);

      if (point.LongitudeRad < -Math.PI) point.LongitudeRad += Geo.TwoPi;
      else if (point.LongitudeRad >= Math.PI) point.LongitudeRad -= Geo.TwoPi;

      return point;
    }

    private GeoPath PathTo(GeoPoint point)
    {
      GeoPath path = new GeoPath();

      double s1 = Math.Sin(LatitudeRad);
      double c1 = Math.Cos(LatitudeRad);
      double s2 = Math.Sin(point.LatitudeRad);
      double c2 = Math.Cos(point.LatitudeRad);
      double sL = Math.Sin(point.LongitudeRad - LongitudeRad);
      double cL = Math.Cos(point.LongitudeRad - LongitudeRad);

      // clamp to avoid NaN from rounding past 1 (e.g. distance from a point to itself)
      double cosDist = Math.Max(-1, Math.Min(1, s1 * s2 + c1 * c2 * cL));
      path.DistanceRad = Math.Acos(cosDist);

      path.AzimuthRad = Math.Atan2(sL * c2, c1 * s2 - s1 * c2 * cL);

      if (path.AzimuthRad < 0) path.AzimuthRad += Geo.TwoPi;
      else if (path.AzimuthRad > Geo.TwoPi) path.AzimuthRad -= Geo.TwoPi;

      return path;
    }

    public override string ToString()
    {
      string ns = LatitudeRad > 0 ? "N" : "S";
      string ew = LongitudeRad > 0 ? "E" : "W";
      return $"{Math.Abs(Latitude):F2} {ns}  {Math.Abs(Longitude):F2} {ew}";
    }

    public static GeoPoint operator +(GeoPoint point, GeoPath path) => point.MoveBy(path);
    public static GeoPath operator -(GeoPoint p2, GeoPoint p1) => p1.PathTo(p2);

    // exact comparison
    public static bool operator ==(GeoPoint p2, GeoPoint p1) => p2.LatitudeRad == p1.LatitudeRad && p2.LongitudeRad == p1.LongitudeRad;
    public static bool operator !=(GeoPoint p2, GeoPoint p1) => p2.LatitudeRad != p1.LatitudeRad || p2.LongitudeRad != p1.LongitudeRad;
  }

  public class Bearing
  {
    public double Azimuth { get; set; }
    public double Elevation { get; set; }
    public Bearing(double azimuth, double elevation)
    {
      Azimuth = azimuth;
      Elevation = elevation;
    }
    public static bool operator ==(Bearing? b2, Bearing? b1) => b2?.Azimuth == b1?.Azimuth && b2?.Elevation == b1?.Elevation;
    public static bool operator !=(Bearing? b2, Bearing? b1) => b2?.Azimuth != b1?.Azimuth || b2?.Elevation != b1?.Elevation;
    public static double AngleBetween(Bearing b1, Bearing b2)
    {
      // haversine formula
      double se = Math.Sin((b2.Elevation - b1.Elevation) * Geo.RinD / 2);
      double ce = Math.Cos(b1.Elevation * Geo.RinD) * Math.Cos(b2.Elevation * Geo.RinD);
      double sa = Math.Sin((b2.Azimuth - b1.Azimuth) * Geo.RinD / 2);
      double a = se * se + ce * sa * sa;
      return Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 2 * Geo.DinR;
    }

    public static double AzimuthDifference(Bearing b1, Bearing b2)
    {
      double diff = Math.Abs(b1.Azimuth - b2.Azimuth);
      if (diff > 180) diff = 360 - diff;
      return diff;
    }
  }
}
