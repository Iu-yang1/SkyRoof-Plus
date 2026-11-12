namespace SkyRoof.Orbit.Orekit;

// P0 local value objects. P1 moves the stable contract into SkyRoof.Orbit.Contracts.
// Units are SI throughout; no Orekit/IKVM types cross this public boundary.
public sealed record OrbitState(
    DateTimeOffset Utc,
    string Frame,
    double XMetres,
    double YMetres,
    double ZMetres,
    double VxMetresPerSecond,
    double VyMetresPerSecond,
    double VzMetresPerSecond);

public sealed record GroundStationSpec(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double EllipsoidalHeightMetres);

public sealed record OrbitObservation(
    DateTimeOffset Utc,
    double AzimuthDegrees,
    double ElevationDegrees,
    double RangeMetres,
    double RangeRateMetresPerSecond,
    string Frame,
    string DataQuality);
