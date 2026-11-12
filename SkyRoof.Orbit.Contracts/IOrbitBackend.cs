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
    IReadOnlyList<OrbitPass> FindPasses(GroundStationSpec ground, DateTimeOffset start, DateTimeOffset end,
        double maskDegrees = 0) => OrbitPassPredictor.FindPasses(this, ground, start, end, maskDegrees);
}
