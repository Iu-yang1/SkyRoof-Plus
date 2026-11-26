using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal Task<OfflineAfDecodeResult> DecodeOfflineAfAsync(OfflineAfDecodeRequest request, CancellationToken token) =>
        dsp.DecodeOfflineAfAsync(request, deadline: DateTime.UtcNow.AddMinutes(3), cancellationToken: token).ResponseAsync;
}
