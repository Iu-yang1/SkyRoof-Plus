using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationDeviceBridge
{
    internal Task<StationRuntimeIdentity> IdentifyAsync(CancellationToken token) =>
        new StationRuntime.StationRuntimeClient(_channel.Value).IdentifyAsync(new(),
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
}
