using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SkyRoof.Remote.Host.Identity;

public sealed class StationUser : IdentityUser
{
    public string? Callsign { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class StationAccessToken
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Digest { get; set; } = "";
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}

public sealed class StationLoginCode
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = "";
    public string Digest { get; set; } = "";
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
}

public sealed class StationIdentityDb(DbContextOptions<StationIdentityDb> options)
    : IdentityDbContext<StationUser>(options)
{
    public DbSet<StationAccessToken> AccessTokens => Set<StationAccessToken>();
    public DbSet<StationLoginCode> LoginCodes => Set<StationLoginCode>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<StationAccessToken>().HasIndex(x => x.Digest).IsUnique();
        builder.Entity<StationAccessToken>().HasIndex(x => new { x.OwnerUserId, x.RevokedUtc });
        builder.Entity<StationLoginCode>().HasIndex(x => x.Digest).IsUnique();
    }
}
