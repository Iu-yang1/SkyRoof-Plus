using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteRotatorPosition(double AzimuthDegrees, double ElevationDegrees,
    bool ReadbackFresh, bool StopTransmitted, bool HasPosition);

public sealed record RemoteParkWaypoint(double AzimuthDegrees, double ElevationDegrees);

public sealed partial class StationDeviceBridge
{
    private DeviceRotator.DeviceRotatorClient RotatorClient => new(_channel.Value);

    public async Task<RemoteRotatorPosition> ReadRotatorAsync(CancellationToken token) =>
        await RotatorCommandAsync((client, ct) =>
            client.ReadAsync(new RotatorRead(), deadline: DateTime.UtcNow.AddSeconds(6),
                cancellationToken: ct).ResponseAsync, token);

    public async Task<RemoteRotatorPosition> PointRotatorAsync(double az, double el,
        CancellationToken token) =>
        await RotatorCommandAsync((client, ct) =>
            client.PointAsync(new RotatorPoint { AzimuthDegrees = az, ElevationDegrees = el },
                deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: ct).ResponseAsync, token);

    public async Task<RemoteRotatorPosition> ParkRotatorAsync(
        IReadOnlyList<RemoteParkWaypoint> waypoints, CancellationToken token)
    {
        var request = new RotatorParkRequest
        {
            ToleranceDegrees = 0.8,
            SegmentTimeoutSeconds = 180
        };
        request.Waypoints.AddRange(waypoints.Select(point => new RotatorPoint
        {
            AzimuthDegrees = point.AzimuthDegrees,
            ElevationDegrees = point.ElevationDegrees
        }));
        return await RotatorCommandAsync((client, ct) =>
            client.ParkAsync(request, cancellationToken: ct).ResponseAsync, token);
    }

    public async Task<RemoteRotatorPosition> StopRotatorAsync(CancellationToken token) =>
        await RotatorCommandAsync((client, ct) =>
            client.StopAsync(new RotatorRead(), deadline: DateTime.UtcNow.AddSeconds(35),
                cancellationToken: ct).ResponseAsync, token);

    private async Task<RemoteRotatorPosition> RotatorCommandAsync(
        Func<DeviceRotator.DeviceRotatorClient, CancellationToken, Task<RotatorPosition>> action,
        CancellationToken token)
    {
        try
        {
            var result = await action(RotatorClient, token);
            return new RemoteRotatorPosition(result.AzimuthDegrees, result.ElevationDegrees,
                result.ReadbackFresh, result.StopTransmitted, result.HasPosition);
        }
        catch (RpcException ex)
        {
            throw new HubException("Rotator: " + ex.Status.Detail);
        }
    }
}
