using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteTrackingStatus
{
    public int NoradNumber { get; init; }
    public long UtcUnixMilliseconds { get; init; }
    public double AzimuthDegrees { get; init; }
    public double ElevationDegrees { get; init; }
    public double RangeMetres { get; init; }
    public double RangeRateMetresPerSecond { get; init; }
    public long RxHz { get; init; }
    public long TxHz { get; init; }
    public string OrbitBackend { get; init; } = "";
    public string DataQuality { get; init; } = "";
    public bool CatApplied { get; init; }
    public bool RotatorApplied { get; init; }
    public long ActualRxHz { get; init; }
    public long ActualTxHz { get; init; }
    public double? ActualAzimuthDegrees { get; init; }
    public double? ActualElevationDegrees { get; init; }
    public string RotatorError { get; init; } = "";
    public string CelestialTarget { get; init; } = "";
    public double OwnEchoDelaySeconds { get; init; }
    public double AfcCorrectionHz { get; init; }
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

    private static RemoteTrackingStatus MapTrackingStatus(TrackingSnapshot result) => new()
        {
            NoradNumber = result.NoradNumber, UtcUnixMilliseconds = result.UtcUnixMilliseconds,
            AzimuthDegrees = result.AzimuthDegrees, ElevationDegrees = result.ElevationDegrees,
            RangeMetres = result.RangeMetres, RangeRateMetresPerSecond = result.RangeRateMetresPerSecond,
            RxHz = result.RxHz, TxHz = result.TxHz, OrbitBackend = result.OrbitBackend,
            DataQuality = result.DataQuality, CatApplied = result.CatApplied, RotatorApplied = result.RotatorApplied,
            ActualRxHz = result.ActualRxHz, ActualTxHz = result.ActualTxHz,
            ActualAzimuthDegrees = result.RotatorReadback?.AzimuthDegrees,
            ActualElevationDegrees = result.RotatorReadback?.ElevationDegrees,
            RotatorError = result.RotatorError, CelestialTarget = result.CelestialTarget,
            OwnEchoDelaySeconds = result.OwnEchoDelaySeconds, AfcCorrectionHz = result.AfcCorrectionHz,
            EmePath = result.EmePath is { } path ? new RemoteEmePath(path.ReceiveRatio, path.TransmitRatio,
                path.IncomingDelaySeconds, path.OutgoingDelaySeconds, path.PeerElevationDegrees) : null
        };
}
