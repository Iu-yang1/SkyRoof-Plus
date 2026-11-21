using Grpc.Core;
using SkyRoof.Remote.Ipc;
using System.Net.Sockets;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService(RadioCatBackend cat, PttLeaseJournal journal,
    ILogger<StationDeviceService> logger)
    : StationDevice.StationDeviceBase
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private DeviceOwner? _owner;
    private long _rxHz;
    private long _txHz;
    private string _rxMode = "";
    private string _txMode = "";
    private ulong _revision;
    private ulong _epoch;
    private bool _digitalPaused;
    private long _lastManualBeat;

    private sealed record DeviceOwner(
        string TransactionId, string SessionId, bool Digital,
        long FrequencyHz, string Mode, string AudioSource, ulong Epoch);

    public override async Task<DeviceSnapshot> Readback(
        DeviceReadbackRequest request, ServerCallContext context)
    {
        await _commands.WaitAsync(context.CancellationToken);
        try
        {
            await RecoverOrphanedPttAsync(context.CancellationToken);
            return await ReadSnapshotAsync("readback", context.CancellationToken);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        { throw FailedCat(e); }
        finally { _commands.Release(); }
    }

    public override async Task<DeviceSnapshot> ReleaseSession(
        DeviceReleaseSessionRequest request, ServerCallContext context)
    {
        await _commands.WaitAsync(context.CancellationToken);
        try
        {
            await RecoverOrphanedPttAsync(context.CancellationToken);
            var result = await ReleaseSessionAsync(request.SessionId, context.CancellationToken);
            if (result.Changed) _revision++;
            return await ReadSnapshotAsync(result.Description, context.CancellationToken);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        { throw FailedCat(e); }
        finally { _commands.Release(); }
    }

    private RpcException FailedCat(Exception e)
    {
        logger.LogWarning(e, "Device CAT transaction failed; hardware state requires readback.");
        return new RpcException(new Status(StatusCode.Unavailable, "CAT transport/readback failed"));
    }

}
