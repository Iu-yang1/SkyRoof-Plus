using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SkyRoof.Remote.Host.Identity;

public static class StationIdentityBootstrap
{
    public static async Task InitializeAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StationIdentityDb>();
        await db.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in new[] { "Viewer", "Operator", "Administrator" })
            if (!await roles.RoleExistsAsync(name))
                Check(await roles.CreateAsync(new IdentityRole(name)));

        var userName = Environment.GetEnvironmentVariable("SKYROOF_BOOTSTRAP_ADMIN_USER");
        var password = Environment.GetEnvironmentVariable("SKYROOF_BOOTSTRAP_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StationUser>>();
        if (await users.FindByNameAsync(userName) is not null) return;
        var admin = new StationUser { UserName = userName, DisplayName = "Station administrator" };
        Check(await users.CreateAsync(admin, password));
        Check(await users.AddToRoleAsync(admin, "Administrator"));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Identity setup failed: " +
                string.Join(", ", result.Errors.Select(x => x.Code)));
    }
}
