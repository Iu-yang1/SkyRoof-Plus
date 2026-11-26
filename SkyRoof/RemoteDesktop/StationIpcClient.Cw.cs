using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal Task<CwRadioStatus> ReadCwAsync(CancellationToken token) =>
        cat.GetCwStatusAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;

    internal Task<CwRadioStatus> SetCwSpeedAsync(double wpm, CancellationToken token) =>
        cat.SetCwSpeedAsync(new CwSpeedRequest { Wpm = wpm },
            deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: token).ResponseAsync;

    internal Task<CwSendReceipt> SendCwAsync(string text, CancellationToken token) =>
        cat.SendCwAsync(new CwSendRequest { Text = text, SessionId = session },
            deadline: DateTime.UtcNow.AddSeconds(225), cancellationToken: token).ResponseAsync;
}
