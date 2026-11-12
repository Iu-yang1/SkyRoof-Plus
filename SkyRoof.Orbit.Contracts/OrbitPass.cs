namespace SkyRoof.Orbit.Contracts;

/// <summary>
/// Passage at or above the requested geometric elevation mask.
/// False ActualAos/ActualLos means search bounds clipped the passage;
/// the corresponding timestamp is then a search boundary, not a physical horizon crossing.
/// </summary>
public sealed record OrbitPass(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset CulminationUtc,
    double MaximumElevationDegrees,
    bool ActualAos,
    bool ActualLos);
