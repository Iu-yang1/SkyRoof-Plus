using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal Task<DeviceSnapshot> ManualPttAsync(DeviceOperation operation, string transaction, CancellationToken token) =>
        cat.ExecuteAsync(new DeviceCommand
        {
            Operation = operation, TransactionId = transaction, SessionId = session
        }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;

    internal bool OwnsPtt(DeviceSnapshot snapshot, string transaction) =>
        snapshot.ActualPtt && snapshot.ActiveSessionId == session &&
        snapshot.ActiveTransactionId == transaction;

    internal Task<DeviceSnapshot> ReleaseSessionAsync(CancellationToken token) =>
        cat.ReleaseSessionAsync(new DeviceReleaseSessionRequest { SessionId = session },
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
}
