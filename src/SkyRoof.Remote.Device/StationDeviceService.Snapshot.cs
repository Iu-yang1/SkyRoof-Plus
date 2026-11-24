using Grpc.Core;
using SkyRoof.Remote.Ipc;
using System.Net.Sockets;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
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
            StatusText = status,
            DeviceProcessId = Environment.ProcessId,
            SupportsDuplexFrequencies = cat.SupportsDuplexFrequencies,
            NativeTxAvailable = cat is NativeIcomCatBackend native && native.Tx is not null
        };
    }


}
