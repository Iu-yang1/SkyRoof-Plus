using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SkyRoof.Remote.Host.Identity;

internal static class StationIdentitySchema
{
    private const int CurrentVersion = 1;

    internal static async Task InitializeAsync(StationIdentityDb db)
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "PRAGMA user_version";
            int version = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (version > CurrentVersion)
                throw new InvalidOperationException($"Station identity schema {version} is newer than supported {CurrentVersion}.");

            if (version == 0)
            {
                command.CommandText = "CREATE INDEX IF NOT EXISTS IX_AccessTokens_OwnerUserId_RevokedUtc ON AccessTokens (OwnerUserId, RevokedUtc)";
                await command.ExecuteNonQueryAsync();
                command.CommandText = "PRAGMA user_version = 1";
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
