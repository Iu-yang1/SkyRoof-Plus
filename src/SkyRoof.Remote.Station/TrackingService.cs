using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class TrackingService(DeviceControlForwarder device, OrbitTrackingCalculator calculator) :
    StationTracking.StationTrackingBase
{
    private readonly SemaphoreSlim _apply = new(1, 1);

    public override Task<TrackingSnapshot> Predict(TrackingRequest request,
        ServerCallContext context) => Task.FromResult(Calculate(request));

    public override Task<GroundTrackResult> GroundTrack(GroundTrackRequest request, ServerCallContext context)
    {
        try { return Task.FromResult(calculator.GroundTrack(request, context.CancellationToken)); }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or IOException or Newtonsoft.Json.JsonException or System.Text.Json.JsonException or org.orekit.errors.OrekitException)
        { throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message)); }
    }

    public override Task<PassSearchResult> PlanPasses(PassSearchRequest request, ServerCallContext context)
    {
        try { return Task.FromResult(calculator.PlanPasses(request, context.CancellationToken)); }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or IOException or Newtonsoft.Json.JsonException or System.Text.Json.JsonException or org.orekit.errors.OrekitException)
        { throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message)); }
    }

    public override async Task<TrackingSnapshot> Apply(TrackingRequest request,
        ServerCallContext context) => await ApplyAtAsync(request, context.CancellationToken);

    internal async Task<TrackingSnapshot> ApplyAtAsync(TrackingRequest request, CancellationToken ct)
    {
        return await ApplyPredictionAsync(request, Calculate(request), ct);
    }

    internal async Task<TrackingSnapshot> ApplyPredictionAsync(TrackingRequest request,
        TrackingSnapshot result, CancellationToken ct, Func<bool>? pointAllowed = null)
    {
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
            result.CatReadbackUtcMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            result.ActualPtt = radio.ActualPtt;
            result.ActualRxMode = radio.RxMode;
            if (request.PointRotator && (pointAllowed?.Invoke() ?? true))
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

    internal TrackingSnapshot Calculate(TrackingRequest request)
    {
        try { return calculator.Predict(request); }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or IOException or Newtonsoft.Json.JsonException or System.Text.Json.JsonException or org.orekit.errors.OrekitException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message));
        }
    }
}
