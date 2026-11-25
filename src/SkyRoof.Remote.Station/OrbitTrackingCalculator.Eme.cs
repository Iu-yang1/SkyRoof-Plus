using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class OrbitTrackingCalculator
{
    private static EmePathStatus? PredictEmePath(TrackingRequest request, IOrbitTrack track,
        GroundStationSpec local, DateTimeOffset utc)
    {
        if (request.EmeDopplerMode != "dx-fixed") return null;
        if (!request.CelestialTarget.Equals("MOON", StringComparison.OrdinalIgnoreCase) ||
            track is not IEmeOrbitTrack eme || request.EmePeer is not { } peer)
            throw new ArgumentException("DX-fixed EME requires Moon ephemeris and a peer ground site.");
        if (!double.IsFinite(peer.LatitudeDegrees) || peer.LatitudeDegrees is < -90 or > 90 ||
            !double.IsFinite(peer.LongitudeDegrees) || peer.LongitudeDegrees is < -180 or > 180 ||
            !double.IsFinite(peer.HeightMetres))
            throw new ArgumentException("Invalid DX ground site.");
        var remote = new GroundStationSpec(peer.LatitudeDegrees, peer.LongitudeDegrees, peer.HeightMetres);
        var path = eme.PredictEmePath(local, remote, utc);
        return new EmePathStatus
        {
            ReceiveRatio = path.ReceiveRatio, TransmitRatio = path.TransmitRatio,
            IncomingDelaySeconds = path.IncomingDelaySeconds, OutgoingDelaySeconds = path.OutgoingDelaySeconds,
            PeerElevationDegrees = track.Observe(remote, utc).ElevationDegrees
        };
    }
}
