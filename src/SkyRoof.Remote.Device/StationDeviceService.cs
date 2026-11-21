using Grpc.Core;
using SkyRoof.Remote.Ipc;
using System.Net.Sockets;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService(RadioCatBackend cat, ILogger<StationDeviceService> logger)
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

    private sealed record DeviceOwner(
        string TransactionId, string SessionId, bool Digital,
        long FrequencyHz, string Mode, string AudioSource, ulong Epoch);

    public override async Task<DeviceSnapshot> Readback(
        DeviceReadbackRequest request, ServerCallContext context)
    {
        await _commands.WaitAsync(context.CancellationToken);
        try { return await ReadSnapshotAsync("readback", context.CancellationToken); }
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
            var result = await ReleaseSessionAsync(request.SessionId, context.CancellationToken);
            if (result.Changed) _revision++;
            return await ReadSnapshotAsync(result.Description, context.CancellationToken);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        { throw FailedCat(e); }
        finally { _commands.Release(); }
    }

    public override async Task<DeviceSnapshot> Execute(
        DeviceCommand request, ServerCallContext context)
    {
        await _commands.WaitAsync(context.CancellationToken);
        try
        {
            var ct = context.CancellationToken;
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
                _ => throw new ArgumentException("Invalid DeviceOperation")
            };
            if (result.Changed) _revision++;
            return await ReadSnapshotAsync(result.Description, ct);
        }
        catch (ArgumentException e) { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
        catch (InvalidOperationException e)
        { throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message)); }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        { throw FailedCat(e); }
        finally { _commands.Release(); }
    }

    private RpcException FailedCat(Exception e)
    {
        logger.LogWarning(e, "Device CAT transaction failed; hardware state requires readback.");
        return new RpcException(new Status(StatusCode.Unavailable, "CAT transport/readback failed"));
    }

    private async Task<DeviceSnapshot> ReadSnapshotAsync(string status, CancellationToken token)
    {
        _rxHz = await cat.ReadRxFrequencyAsync(token);
        _txHz = await cat.ReadTxFrequencyAsync(token);
        var ptt = await cat.ReadPttAsync(token);
        return new DeviceSnapshot
        {
            RxFrequencyHz = _rxHz,
            TxFrequencyHz = _txHz,
            RxMode = _rxMode,
            TxMode = _txMode,
            ActualPtt = ptt,
            ActiveTransactionId = _owner?.TransactionId ?? "",
            ActiveSessionId = _owner?.SessionId ?? "",
            TransactionEpoch = _owner?.Epoch ?? _epoch,
            Revision = _revision,
            DigitalAutomationPaused = _digitalPaused,
            Result = status,
            DeviceProcessId = Environment.ProcessId
        };
    }

    private async Task<(string Description, bool Changed)> TuneRxAsync(DeviceCommand command, CancellationToken ct)
    {
        if (command.FrequencyHz <= 0) throw new ArgumentException("RX frequency must be positive.");
        await cat.SetRxFrequencyAsync(command.FrequencyHz, ct);
        return ("rx-tuned", true);
    }
    private async Task<(string Description, bool Changed)> TuneTxAsync(DeviceCommand command, CancellationToken ct)
    {
        if (command.FrequencyHz <= 0) throw new ArgumentException("TX frequency must be positive.");
        await cat.SetTxFrequencyAsync(command.FrequencyHz, ct);
        _txHz = command.FrequencyHz;
        if (_owner is not null) _owner = _owner with { FrequencyHz = command.FrequencyHz };
        return ("tx-tuned", true);
    }
    private async Task<(string Description, bool Changed)> SetRxModeAsync(DeviceCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Mode)) throw new ArgumentException("RX mode is required.");
        await cat.SetRxModeAsync(command.Mode, ct);
        _rxMode = command.Mode;
        return ("rx-mode-requested", true);
    }
    private async Task<(string Description, bool Changed)> SetTxModeAsync(DeviceCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Mode)) throw new ArgumentException("TX mode is required.");
        await cat.SetTxModeAsync(command.Mode, ct);
        _txMode = command.Mode;
        if (_owner is not null) _owner = _owner with { Mode = command.Mode };
        return ("tx-mode-requested", true);
    }

    private static void RequireTransaction(DeviceCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TransactionId) ||
            string.IsNullOrWhiteSpace(command.SessionId))
            throw new ArgumentException("Transaction and session are required.");
    }
}
