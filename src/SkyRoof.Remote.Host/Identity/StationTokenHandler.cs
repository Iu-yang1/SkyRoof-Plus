using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SkyRoof.Remote.Host.Identity;

public sealed class StationTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    StationIdentityDb db,
    UserManager<StationUser> users)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var raw = authorization["Bearer ".Length..].Trim();
        if (raw.Length < 20) return AuthenticateResult.Fail("Invalid credential.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        var credential = await db.AccessTokens.AsNoTracking().SingleOrDefaultAsync(t =>
            t.Digest == digest && t.RevokedUtc == null);
        if (credential is null || credential.ExpiresUtc <= DateTimeOffset.UtcNow)
            return AuthenticateResult.Fail("Credential expired or revoked.");
        var user = await users.FindByIdAsync(credential.OwnerUserId);
        if (user is null || user.LockoutEnd > DateTimeOffset.UtcNow)
            return AuthenticateResult.Fail("Account unavailable.");
        var roles = await users.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Id),
            new("skyroof_credential", credential.Id.ToString("N"))
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
