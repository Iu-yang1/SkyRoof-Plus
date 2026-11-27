using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class StationRuntimeService : StationRuntime.StationRuntimeBase
{
    private readonly string instance = Guid.NewGuid().ToString("N");
    public override Task<StationRuntimeIdentity> Identify(StationRuntimeQuery request, ServerCallContext context) =>
        Task.FromResult(new StationRuntimeIdentity
        {
            StationId = Environment.GetEnvironmentVariable("SKYROOF_STATION_ID")?.Trim() ?? "default",
            InstanceId = instance
        });
}
