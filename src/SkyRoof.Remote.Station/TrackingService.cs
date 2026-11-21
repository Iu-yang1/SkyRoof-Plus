using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class TrackingService(DeviceControlForwarder device) :
    StationTracking.StationTrackingBase
{
    private readonly SemaphoreSlim _apply = new(1, 1);

    public override Task<TrackingSnapshot> Predict(TrackingRequest request,
        ServerCallContext context) => Task.FromResult(Calculate(request));

    public override async Task<TrackingSnapshot> Apply(TrackingRequest request,
        ServerCallContext context) => await ApplyAtAsync(request, context.CancellationToken);

    internal async Task<TrackingSnapshot> ApplyAtAsync(TrackingRequest request, CancellationToken ct)
    {
        var result = Calculate(request);
        if (result.ElevationDegrees < request.ElevationMaskDegrees) return result;
        await _apply.WaitAsync(ct);
        try
        {
            var radio = await device.Cat.ExecuteAsync(new DeviceCommand
            {
                Operation = DeviceOperation.DeviceTrackFrequencies,
                FrequencyHz = result.RxHz,
                TxFrequencyHz = result.TxHz
            }, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: ct);
            result.CatApplied = true;
            result.ActualRxHz = radio.RxFrequencyHz;
            result.ActualTxHz = radio.TxFrequencyHz;
            if (request.PointRotator)
            {
                try
                {
                    result.RotatorReadback = await device.Rotator.PointAsync(new RotatorPoint
                    {
                        AzimuthDegrees = result.AzimuthDegrees,
                        ElevationDegrees = result.ElevationDegrees
                    }, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: ct);
                    result.RotatorApplied = true;
                }
                catch (RpcException ex) when (!ct.IsCancellationRequested)
                {
                    result.RotatorError = ex.Status.Detail;
                }
            }
            return result;
        }
        finally { _apply.Release(); }
    }

    private static TrackingSnapshot Calculate(TrackingRequest request)
    {
        try { return OrbitTrackingCalculator.Predict(request); }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message));
        }
    }
}
