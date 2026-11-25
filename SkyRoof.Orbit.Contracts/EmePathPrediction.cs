namespace SkyRoof.Orbit.Contracts;

/// <summary>Frequency ratios and geometric light times at the local reception and emission epochs.</summary>
public sealed record EmePathPrediction(double ReceiveRatio, double TransmitRatio,
    double IncomingDelaySeconds, double OutgoingDelaySeconds);

public interface IEmeOrbitTrack
{
    EmePathPrediction PredictEmePath(GroundStationSpec local, GroundStationSpec peer, DateTimeOffset utc);
}
