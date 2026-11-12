using SGPdotNET.Observation;

namespace SkyRoof;

/// <summary>
/// Corrects the radial-velocity calculation in SGP.NET 1.6.0.
/// The upstream v1.6.0 topocentric calculator projects target velocity but
/// omits the ground station's ECI rotational velocity. This calculation
/// uses the same model's observer ECI position and velocity instead.
/// No change is made to TLE/SGP4 propagation, look angles, CAT or rotator protocols.
/// </summary>
internal static class SgpNetRangeRateCorrection
{
    internal static TopocentricObservation Observe(
        GroundStation station, Satellite satellite, DateTime utc)
    {
        var target = satellite.Predict(utc);
        var original = station.Observe(target, utc);
        var origin = station.Location.ToEci(utc);

        var relativePosition = target.Position - origin.Position;
        var relativeVelocity = target.Velocity - origin.Velocity;
        double radialVelocityKmPerSecond =
            relativePosition.Dot(relativeVelocity) / relativePosition.Length;

        return new TopocentricObservation(
            original.Azimuth, original.Elevation,
            original.Range, radialVelocityKmPerSecond, station.Location);
    }
}
