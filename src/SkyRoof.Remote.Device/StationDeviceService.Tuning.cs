using Grpc.Core;
using SkyRoof.Remote.Ipc;
using System.Net.Sockets;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
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
