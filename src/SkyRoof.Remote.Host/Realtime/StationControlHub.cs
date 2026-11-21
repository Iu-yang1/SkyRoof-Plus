using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SkyRoof.Remote.Host.Realtime;

[Authorize]
public sealed class StationControlHub : Hub
{
    public Task<StationConnectionInfo> GetConnectionInfo() =>
        Task.FromResult(new StationConnectionInfo(
            "skyroof.remote.v1", "foundation-prototype", Context.UserIdentifier ?? ""));

    [Authorize(Roles = "Operator,Administrator")]
    public Task<string> PingCommandPlane(string requestId) =>
        Task.FromResult(requestId);
}

public sealed record StationConnectionInfo(
    string Protocol,
    string RuntimeMode,
    string PrincipalId);
