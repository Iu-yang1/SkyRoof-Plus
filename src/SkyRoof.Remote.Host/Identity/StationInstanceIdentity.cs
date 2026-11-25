using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace SkyRoof.Remote.Host.Identity;

internal sealed record StationInstanceIdentity(string Id, string CookieName)
{
    internal static StationInstanceIdentity Configure(IServiceCollection services, string databaseDirectory)
    {
        string? configured = Environment.GetEnvironmentVariable("SKYROOF_STATION_ID");
        if (string.IsNullOrWhiteSpace(configured)) return new("default", "skyroof.station");
        string id = configured.Trim();
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..16].ToLowerInvariant();
        string keys = Environment.GetEnvironmentVariable("SKYROOF_AUTH_KEYS_DIRECTORY") ??
            Path.Combine(databaseDirectory, "auth-keys-" + suffix);
        Directory.CreateDirectory(keys);
        var protection = services.AddDataProtection()
            .SetApplicationName("SkyRoof.Remote." + id)
            .PersistKeysToFileSystem(new DirectoryInfo(keys));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        return new(id, "skyroof.station." + suffix);
    }
}
