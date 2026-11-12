namespace SkyRoof.Orbit.Contracts;

/// <summary>WGS-84 geodetic location; longitude/latitude degrees and ellipsoid height metres.</summary>
public sealed record OrbitGeodetic(
    DateTimeOffset Utc,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double EllipsoidalHeightMetres);
