using SGPdotNET.CoordinateSystem;
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
}

public sealed class SgpNetTleTrack : IOrbitTrack
{
    private readonly Satellite satellite;

    public SgpNetTleTrack(string line1, string line2)
    {
        satellite = new Satellite(new Tle(line1, line2));
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

    public OrbitObservation Observe(GroundStationSpec ground, DateTimeOffset utc)
    {
        var time = utc.ToUniversalTime();
        var site = new GeodeticCoordinate(
            Angle.FromDegrees(ground.LatitudeDegrees),
            Angle.FromDegrees(ground.LongitudeDegrees),
            ground.EllipsoidalHeightMetres / 1000.0);
        var observation = new GroundStation(site).Observe(satellite, time.UtcDateTime);
        return new OrbitObservation(
            time,
            observation.Azimuth.Degrees,
            observation.Elevation.Degrees,
            observation.Range * 1000.0,
            observation.RangeRate * 1000.0,
            "SGPNET_TOPOCENTRIC_GMST",
            "SGPNET_NO_EOP");
    }
}
