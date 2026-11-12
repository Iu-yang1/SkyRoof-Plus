using SGPdotNET.CoordinateSystem;
using SGPdotNET.Parsers;
using SGPdotNET.Observation;
using SGPdotNET.TLE;
using SGPdotNET.Util;
using SkyRoof.Orbit.Contracts;

namespace SkyRoof.Orbit.SgpNet;

public sealed class SgpNetOrbitBackend : IOrbitBackend
{
    public string BackendId => "sgpnet";

    public IOrbitTrack CreateTleTrack(string line1, string line2) =>
        new SgpNetTleTrack(line1, line2);

    public IOrbitTrack CreateOmmTrack(OrbitMeanElements elements) =>
        new SgpNetTleTrack(elements);
}

public sealed class SgpNetTleTrack : IOrbitTrack
{
    private readonly Satellite satellite;

    public SgpNetTleTrack(string line1, string line2)
    {
        satellite = new Satellite(new Tle(line1, line2));
    }

    public SgpNetTleTrack(OrbitMeanElements elements)
    {
        var omm = new OmmData
        {
            NoradCatID = checked((uint)elements.NoradNumber),
            ObjectID = elements.ObjectId,
            ClassificationType = elements.Classification.ToString(),
            Epoch = elements.EpochUtc.UtcDateTime,
            MeanMotion = elements.MeanMotionRevolutionsPerDay,
            MeanMotionDot = elements.MeanMotionDerivativeRevolutionsPerDay2,
            MeanMotionDDot = elements.MeanMotionSecondDerivativeRevolutionsPerDay3,
            Eccentricity = elements.Eccentricity,
            Inclination = elements.InclinationDegrees,
            RAOfAscNode = elements.RightAscensionDegrees,
            ArgOfPericenter = elements.ArgumentOfPerigeeDegrees,
            MeanAnomaly = elements.MeanAnomalyDegrees,
            BStar = elements.BStar,
            EphemerisType = elements.EphemerisType,
            ElementSetNo = checked((uint)elements.ElementSetNumber),
            RevAtEpoch = checked((uint)elements.RevolutionNumberAtEpoch)
        };
        satellite = new Satellite(new Tle(omm));
    }

    public int NoradNumber => checked((int)satellite.Tle.NoradNumber);

    public OrbitState PredictTeme(DateTimeOffset utc)
    {
        var time = utc.ToUniversalTime();
        var pv = satellite.Predict(time.UtcDateTime);
        return new OrbitState(
            time, "TEME",
            pv.Position.X * 1000.0, pv.Position.Y * 1000.0, pv.Position.Z * 1000.0,
            pv.Velocity.X * 1000.0, pv.Velocity.Y * 1000.0, pv.Velocity.Z * 1000.0);
    }

    public OrbitGeodetic PredictGeodetic(DateTimeOffset utc)
    {
        var time = utc.ToUniversalTime();
        var position = satellite.Predict(time.UtcDateTime).ToGeodetic();
        return new OrbitGeodetic(
            time, position.Latitude.Degrees, position.Longitude.Degrees,
            position.Altitude * 1000.0);
    }

    public OrbitObservation Observe(GroundStationSpec ground, DateTimeOffset utc)
    {
        var time = utc.ToUniversalTime();
        var site = new GeodeticCoordinate(
            Angle.FromDegrees(ground.LatitudeDegrees),
            Angle.FromDegrees(ground.LongitudeDegrees),
            ground.EllipsoidalHeightMetres / 1000.0);
        var groundStation = new GroundStation(site);
        var pv = satellite.Predict(time.UtcDateTime);
        var observation = groundStation.Observe(pv, time.UtcDateTime);
        var observer = site.ToEci(time.UtcDateTime);
        var lineOfSight = pv.Position - observer.Position;
        var relativeVelocity = pv.Velocity - observer.Velocity;
        double correctedRangeRateKmPerSec =
            lineOfSight.Dot(relativeVelocity) / lineOfSight.Length;
        return new OrbitObservation(
            time,
            observation.Azimuth.Degrees,
            observation.Elevation.Degrees,
            observation.Range * 1000.0,
            correctedRangeRateKmPerSec * 1000.0,
            "SGPNET_TOPOCENTRIC_GMST",
            "SGPNET_NO_EOP");
    }
}
