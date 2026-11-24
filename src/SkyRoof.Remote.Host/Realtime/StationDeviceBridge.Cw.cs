using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationDeviceBridge
{
    public async Task<CwRadioStatus> GetCwStatusAsync(CancellationToken ct) =>
        await Client.GetCwStatusAsync(new CwStatusRequest(),
            deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: ct);

    public async Task<CwRadioStatus> SetCwSpeedAsync(double wpm, CancellationToken ct) =>
        await Client.SetCwSpeedAsync(new CwSpeedRequest { Wpm = wpm },
            deadline: DateTime.UtcNow.AddSeconds(12), cancellationToken: ct);

    public async Task<CwSendReceipt> SendCwAsync(CwSendRequest request, CancellationToken ct) =>
        await Client.SendCwAsync(request, deadline: DateTime.UtcNow.AddSeconds(240), cancellationToken: ct);
}
