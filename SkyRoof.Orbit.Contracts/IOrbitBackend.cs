namespace SkyRoof.Orbit.Contracts;

public interface IOrbitBackend
{
    string BackendId { get; }
    IOrbitTrack CreateTleTrack(string line1, string line2);
    IOrbitTrack CreateOmmTrack(OrbitMeanElements elements);
}

public interface IOrbitTrack
{
    int NoradNumber { get; }
    OrbitState PredictTeme(DateTimeOffset utc);
    OrbitObservation Observe(GroundStationSpec ground, DateTimeOffset utc);
}
