using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SkyRoof.Remote.Host.Identity;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

[Authorize]
public sealed class StationControlHub(
    StationDeviceBridge device,
    StationIdentityDb db,
    UserManager<StationUser> users,
    ILogger<StationControlHub> logger) : Hub
{
    private const string ManualPttHeld = "skyroof:manual-ptt";
    public Task<StationConnectionInfo> GetConnectionInfo() =>
        Task.FromResult(new StationConnectionInfo(
            "skyroof.remote.v1", "foundation-prototype", Context.UserIdentifier ?? ""));

    [Authorize(Roles = "Operator,Administrator")]
    public Task<string> PingCommandPlane(string requestId) =>
        Task.FromResult(requestId);

    public async Task<RemoteCatStatus> GetCatReadback()
    {
        await VerifyCurrentSessionAsync(requireOperator: false);
        return await device.ReadbackAsync(Context.ConnectionAborted);
    }

    public async Task<RemoteCatStatus> ExecuteCatCommand(RemoteCatCommand command)
    {
        await VerifyCurrentSessionAsync(requireOperator: true);
        if (command.Operation is DeviceOperation.DeviceOpUnknown)
            throw new HubException("Invalid radio operation.");
        var status = await device.ExecuteAsync(command, Context.ConnectionId,
            Context.ConnectionAborted);
        if (command.Operation == DeviceOperation.DeviceManualBegin && status.ActualPtt
            && status.ActiveSessionId == Context.ConnectionId)
            Context.Items[ManualPttHeld] = true;
        if (command.Operation is DeviceOperation.DeviceManualEnd or DeviceOperation.DeviceEmergencyRx)
            Context.Items.Remove(ManualPttHeld);
        return status;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.ContainsKey(ManualPttHeld))
        {
            try { await device.ReleaseSessionAsync(Context.ConnectionId, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Unable to release disconnected PTT session"); }
        }
        await base.OnDisconnectedAsync(exception);
    }

    private async Task VerifyCurrentSessionAsync(bool requireOperator)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new HubException("Session identity missing.");
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.LockoutEnd > DateTimeOffset.UtcNow)
            throw new HubException("Station account unavailable.");
        var credentialId = Context.User?.FindFirstValue("skyroof_credential");
        if (credentialId is not null)
        {
            if (!Guid.TryParse(credentialId, out var tokenId))
                throw new HubException("Session token is invalid.");
            var token = await db.AccessTokens.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == tokenId && x.OwnerUserId == userId && x.RevokedUtc == null);
            if (token is null || token.ExpiresUtc <= DateTimeOffset.UtcNow)
                throw new HubException("Session token expired or revoked.");
        }
        if (requireOperator)
        {
            var roles = await users.GetRolesAsync(user);
            if (!roles.Contains("Operator") && !roles.Contains("Administrator"))
                throw new HubException("Operator permission required.");
        }
    }
}

public sealed record StationConnectionInfo(string Protocol, string RuntimeMode, string PrincipalId);
