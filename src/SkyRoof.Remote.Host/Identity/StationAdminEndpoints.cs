using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SkyRoof.Remote.Host.Identity;

public sealed record StationCreateUser(string UserName, string Password, string Role);
public sealed record StationSetRole(string Role);

public static class StationAdminEndpoints
{
    public static void MapStationAdministration(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .RequireAuthorization(policy => policy.RequireRole("Administrator"));
        group.MapPost("/users", CreateUserAsync);
        group.MapGet("/users", ListUsersAsync);
        group.MapPut("/users/{id}/role", SetRoleAsync);
        group.MapPost("/users/{id}/revoke-tokens", RevokeTokensAsync);
    }

    private static async Task<IResult> CreateUserAsync(
        StationCreateUser request, UserManager<StationUser> users,
        RoleManager<IdentityRole> roles)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) ||
            !await roles.RoleExistsAsync(request.Role))
            return Results.BadRequest(new { error = "Unknown username or role." });
        var user = new StationUser { UserName = request.UserName };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Results.BadRequest(new { error = result.Errors.Select(x => x.Code) });
        result = await users.AddToRoleAsync(user, request.Role);
        if (!result.Succeeded)
        {
            await users.DeleteAsync(user);
            return Results.BadRequest(new { error = result.Errors.Select(x => x.Code) });
        }
        return Results.Created("/api/v1/admin/users/" + user.Id,
            new { user.Id, user.UserName, request.Role });
    }

    private static async Task<IResult> ListUsersAsync(
        UserManager<StationUser> users)
    {
        var usersInStation = await users.Users.AsNoTracking()
            .OrderBy(x => x.UserName).ToListAsync();
        var entries = new List<object>(usersInStation.Count);
        foreach (var user in usersInStation)
            entries.Add(new {
                user.Id, user.UserName, user.Callsign,
                Roles = await users.GetRolesAsync(user)
            });
        return Results.Ok(entries);
    }

    private static async Task<IResult> SetRoleAsync(
        string id, StationSetRole request,
        UserManager<StationUser> users, RoleManager<IdentityRole> roles)
    {
        if (!await roles.RoleExistsAsync(request.Role)) return Results.BadRequest();
        var user = await users.FindByIdAsync(id);
        if (user is null) return Results.NotFound();
        var current = await users.GetRolesAsync(user);
        var removed = await users.RemoveFromRolesAsync(user, current);
        if (!removed.Succeeded)
            return Results.Problem("Cannot replace station access role.");
        var assigned = await users.AddToRoleAsync(user, request.Role);
        if (!assigned.Succeeded)
            return Results.Problem("Cannot assign station access role.");
        return Results.Ok(new { user.Id, request.Role });
    }

    private static async Task<IResult> RevokeTokensAsync(
        string id, StationIdentityDb db, UserManager<StationUser> users)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Results.NotFound();
        var now = DateTimeOffset.UtcNow;
        var count = await db.AccessTokens.Where(t => t.OwnerUserId == id &&
            t.RevokedUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedUtc, now));
        await users.UpdateSecurityStampAsync(user);
        return Results.Ok(new { revoked = count });
    }
}
