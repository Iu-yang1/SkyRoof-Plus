using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteTrackingStatus(int NoradNumber, long UtcUnixMilliseconds,
    double AzimuthDegrees, double ElevationDegrees, double RangeMetres,
    double RangeRateMetresPerSecond, long RxHz, long TxHz,
    string OrbitBackend, string DataQuality, bool CatApplied, bool RotatorApplied,
    long ActualRxHz, long ActualTxHz, double? ActualAzimuthDegrees,
    double? ActualElevationDegrees, string RotatorError, string CelestialTarget = "", double OwnEchoDelaySeconds = 0)
{
    public RemoteEmePath? EmePath { get; init; }
}

public sealed record RemoteEmePath(double ReceiveRatio, double TransmitRatio,
    double IncomingDelaySeconds, double OutgoingDelaySeconds, double PeerElevationDegrees);

public sealed partial class StationDeviceBridge
{
    public async Task<RemoteTrackingStatus> TrackAsync(
        RemoteTrackingRequest input, bool apply, CancellationToken ct)
    {
        var request = MapTrackingRequest(input);
        var client = new StationTracking.StationTrackingClient(_channel.Value);
        try
        {
            var result = apply
                ? await client.ApplyAsync(request, deadline: DateTime.UtcNow.AddSeconds(18), cancellationToken: ct)
                : await client.PredictAsync(request, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);
            return MapTrackingStatus(result);
        }
        catch (RpcException ex) { throw new HubException("Tracking failed: " + ex.Status.Detail); }
    }
    private static TrackingRequest MapTrackingRequest(RemoteTrackingRequest input) => new TrackingRequest
        {
            TleLine1 = input.TleLine1,
            TleLine2 = input.TleLine2,
            LatitudeDegrees = input.LatitudeDegrees,
            LongitudeDegrees = input.LongitudeDegrees,
            HeightMetres = input.HeightMetres,
            UtcUnixMilliseconds = input.UtcUnixMilliseconds,
            DownlinkHz = input.DownlinkHz,
            UplinkHz = input.UplinkHz,
            CorrectDownlink = input.CorrectDownlink,
            CorrectUplink = input.CorrectUplink,
            PointRotator = input.PointRotator,
            ElevationMaskDegrees = input.ElevationMaskDegrees,
            RadioLink = input.RadioLink?.ToIpc(),
            CatalogSatelliteId = input.CatalogSatelliteId ?? "",
            CelestialTarget = input.CelestialTarget ?? "", EmeDopplerMode = input.EmeDopplerMode ?? "",
            EmePeer = input.EmePeer is { } peer ? new EmePeerSite { LatitudeDegrees = peer.LatitudeDegrees,
                LongitudeDegrees = peer.LongitudeDegrees, HeightMetres = peer.HeightMetres } : null
        };

    private static RemoteTrackingStatus MapTrackingStatus(TrackingSnapshot result) => new RemoteTrackingStatus(result.NoradNumber, result.UtcUnixMilliseconds,
                result.AzimuthDegrees, result.ElevationDegrees, result.RangeMetres,
                result.RangeRateMetresPerSecond, result.RxHz, result.TxHz,
                result.OrbitBackend, result.DataQuality, result.CatApplied, result.RotatorApplied,
                result.ActualRxHz, result.ActualTxHz, result.RotatorReadback?.AzimuthDegrees,
                result.RotatorReadback?.ElevationDegrees, result.RotatorError, result.CelestialTarget, result.OwnEchoDelaySeconds)
            {
                EmePath = result.EmePath is { } path ? new RemoteEmePath(path.ReceiveRatio, path.TransmitRatio,
                    path.IncomingDelaySeconds, path.OutgoingDelaySeconds, path.PeerElevationDegrees) : null
            };

}
