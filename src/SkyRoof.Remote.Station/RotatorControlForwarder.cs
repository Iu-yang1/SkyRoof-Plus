using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

/// <summary>
/// Host never opens hardware TCP sockets; Station forwards validated commands
/// to the sole Device process. No non-idempotent commands are replayed.
/// </summary>
public sealed class RotatorControlForwarder(DeviceControlForwarder device)
    : DeviceRotator.DeviceRotatorBase
{
    public override async Task<RotatorPosition> Read(RotatorRead request,
        ServerCallContext context) => await device.Rotator.ReadAsync(request,
            deadline: DateTime.UtcNow.AddSeconds(5),
            cancellationToken: context.CancellationToken);

    public override async Task<RotatorPosition> Point(RotatorPoint request,
        ServerCallContext context) => await device.Rotator.PointAsync(request,
            deadline: DateTime.UtcNow.AddSeconds(8),
            cancellationToken: context.CancellationToken);

    public override async Task<RotatorPosition> Park(RotatorParkRequest request,
        ServerCallContext context) => await device.Rotator.ParkAsync(request,
            cancellationToken: context.CancellationToken);

    public override async Task<RotatorPosition> Stop(RotatorRead request,
        ServerCallContext context) => await device.Rotator.StopAsync(request,
            deadline: DateTime.UtcNow.AddSeconds(8),
            cancellationToken: context.CancellationToken);
}
