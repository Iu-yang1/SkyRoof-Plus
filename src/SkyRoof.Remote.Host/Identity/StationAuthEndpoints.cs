using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SkyRoof.Remote.Host.Identity;

public sealed record LoginRequest(string UserName, string Password);
public sealed record NameRequest(string Name);
public sealed record CodeRedeemRequest(string Code);

public static class StationAuthEndpoints
{
    public static void MapStationAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth");
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/redeem", RedeemAsync);
        auth.MapPost("/tokens", CreateTokenAsync).RequireAuthorization();
        auth.MapDelete("/tokens/{id:guid}", RevokeTokenAsync).RequireAuthorization();
        auth.MapPost("/login-codes", CreateCodeAsync).RequireAuthorization();
        auth.MapPost("/logout", async (SignInManager<StationUser> signin) =>
        {
            await signin.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, SignInManager<StationUser> signin)
    {
        var result = await signin.PasswordSignInAsync(request.UserName,
            request.Password, isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? Results.Ok(new { authenticated = true }) : Results.Unauthorized();
    }

    private static async Task<IResult> CreateTokenAsync(
        NameRequest request, ClaimsPrincipal principal, StationIdentityDb db)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest();
        var raw = "srt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var record = new StationAccessToken
        {
            Id = Guid.NewGuid(), Name = request.Name, OwnerUserId = userId,
            Digest = Digest(raw), ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        };
        db.AccessTokens.Add(record);
        await db.SaveChangesAsync();
        return Results.Ok(new { record.Id, Token = raw, record.ExpiresUtc });
    }

    private static async Task<IResult> RevokeTokenAsync(
        Guid id, ClaimsPrincipal principal, StationIdentityDb db)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var updated = await db.AccessTokens.Where(t => t.Id == id && t.OwnerUserId == userId
            && t.RevokedUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedUtc, DateTimeOffset.UtcNow));
        return updated == 1 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> CreateCodeAsync(
        ClaimsPrincipal principal, StationIdentityDb db)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        db.LoginCodes.Add(new StationLoginCode
        {
            Id = Guid.NewGuid(),
            OwnerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!,
            Digest = Digest(code),
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(2)
        });
        await db.SaveChangesAsync();
        return Results.Ok(new { Code = code, ExpiresInSeconds = 120 });
    }

    private static async Task<IResult> RedeemAsync(
        CodeRedeemRequest request, StationIdentityDb db,
        UserManager<StationUser> users, SignInManager<StationUser> signin)
    {
        var digest = Digest(request.Code);
        var now = DateTimeOffset.UtcNow;
        var code = await db.LoginCodes.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Digest == digest && x.ConsumedUtc == null);
        if (code is null || code.ExpiresUtc <= now) return Results.Unauthorized();
        // Atomic consume protects against concurrent reuse across clients.
        var changed = await db.LoginCodes.Where(x => x.Id == code.Id && x.ConsumedUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.ConsumedUtc, now));
        if (changed != 1) return Results.Unauthorized();
        var user = await users.FindByIdAsync(code.OwnerUserId);
        if (user is null) return Results.Unauthorized();
        await signin.SignInAsync(user, isPersistent: false);
        return Results.Ok(new { authenticated = true });
    }

    private static string Digest(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
