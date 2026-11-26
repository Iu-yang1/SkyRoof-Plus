using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    private StationAfc.StationAfcClient Afc => new(channel);
    internal Task<AfcSessionStatus> OpenAfcAsync(AfcOpenRequest request, CancellationToken token) =>
        Afc.OpenAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
    internal Task<AfcSessionStatus> UpdateAfcAsync(AfcUpdateRequest request, CancellationToken token) =>
        Afc.UpdateAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
    internal Task<AfcSessionStatus> CloseAfcAsync(string id, CancellationToken token) =>
        Afc.CloseAsync(new AfcCloseRequest { SessionId = id }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
}
