using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationDeviceBridge
{
    private static RemoteCatStatus Map(DeviceSnapshot result) => new()
    {
        RxFrequencyHz = result.RxFrequencyHz,
        TxFrequencyHz = result.TxFrequencyHz,
        RxMode = result.RxMode,
        TxMode = result.TxMode,
        ActualPtt = result.ActualPtt,
        ActiveTransactionId = result.ActiveTransactionId,
        ActiveSessionId = result.ActiveSessionId,
        Epoch = result.TransactionEpoch,
        Revision = result.Revision,
        DigitalAutomationPaused = result.DigitalAutomationPaused,
        Outcome = result.StatusText,
        SupportsDuplexFrequencies = result.SupportsDuplexFrequencies,
        NativeTxAvailable = result.NativeTxAvailable
    };
}
