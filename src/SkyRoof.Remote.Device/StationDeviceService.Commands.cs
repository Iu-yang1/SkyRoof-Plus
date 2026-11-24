using Grpc.Core;
using SkyRoof.Remote.Ipc;
using System.Net.Sockets;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    public override async Task<DeviceSnapshot> Execute(
        DeviceCommand request, ServerCallContext context)
    {
        await _commands.WaitAsync(context.CancellationToken);
        try
        {
            var ct = context.CancellationToken;
            await RecoverOrphanedPttAsync(ct);
            var result = request.Operation switch
            {
                DeviceOperation.DeviceRxFrequency => await TuneRxAsync(request, ct),
                DeviceOperation.DeviceTxFrequency => await TuneTxAsync(request, ct),
                DeviceOperation.DeviceRxMode => await SetRxModeAsync(request, ct),
                DeviceOperation.DeviceTxMode => await SetTxModeAsync(request, ct),
                DeviceOperation.DeviceManualBegin => await BeginAsync(request, false, ct),
                DeviceOperation.DeviceDigitalBegin => await BeginAsync(request, true, ct),
                DeviceOperation.DeviceManualEnd => await EndAsync(request, false, ct),
                DeviceOperation.DeviceDigitalEnd => await EndAsync(request, true, ct),
                DeviceOperation.DeviceEmergencyRx => await StopAsync(ct),
                DeviceOperation.DeviceResumeDigitalTx => ResumeDigital(),
                DeviceOperation.DeviceManualHeartbeat => RenewManualLease(request),
                DeviceOperation.DeviceTrackFrequencies => await TrackFrequenciesAsync(request, ct),
                _ => throw new ArgumentException("Invalid DeviceOperation")
            };
            if (result.Changed) _revision++;
            return await ReadSnapshotAsync(result.Description, ct);
        }
        catch (ArgumentException e) { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
        catch (InvalidOperationException e)
        { throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message)); }
        catch (Exception e) when (e is IOException or SocketException or TimeoutException or OperationCanceledException)
        { throw FailedCat(e); }
        finally { _commands.Release(); }
    }


}
