namespace SkyRoof.Orbit.Contracts;

/// <summary>
/// CCSDS GP/OMM mean elements for SGP4. Angles are degrees; motion derivatives
/// are revolutions/day² and revolutions/day³ (not TLE's half/sixth stored terms).
/// No fixed-column TLE or third-party object is required for 6-digit NORAD IDs.
/// </summary>
public sealed record OrbitMeanElements(
    int NoradNumber,
    string ObjectId,
    char Classification,
    DateTimeOffset EpochUtc,
    double MeanMotionRevolutionsPerDay,
    double MeanMotionDerivativeRevolutionsPerDay2,
    double MeanMotionSecondDerivativeRevolutionsPerDay3,
    double Eccentricity,
    double InclinationDegrees,
    double RightAscensionDegrees,
    double ArgumentOfPerigeeDegrees,
    double MeanAnomalyDegrees,
    double BStar,
    int EphemerisType,
    int ElementSetNumber,
    int RevolutionNumberAtEpoch);
