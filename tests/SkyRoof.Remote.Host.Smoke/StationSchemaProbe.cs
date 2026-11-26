using Microsoft.Data.Sqlite;

internal static class StationSchemaProbe
{
    internal static async Task PrepareLegacyAsync(string directory)
    {
        await using var db = await OpenAsync(directory);
        await using var transaction = await db.BeginTransactionAsync();
        await using var cmd = db.CreateCommand();
        cmd.Transaction = (SqliteTransaction)transaction;
        cmd.CommandText = "DROP INDEX IX_AccessTokens_OwnerUserId_RevokedUtc; PRAGMA user_version = 0;";
        await cmd.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    internal static async Task VerifyMigratedAsync(string directory)
    {
        await using var db = await OpenAsync(directory);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
            throw new InvalidDataException("Legacy station schema version was not migrated.");
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_AccessTokens_OwnerUserId_RevokedUtc'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
            throw new InvalidDataException("Station token owner index was not migrated.");
        Console.WriteLine("PASS: existing SQLite identity database migrated in place.");
    }

    private static async Task<SqliteConnection> OpenAsync(string directory)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "auth.db"), Mode = SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}
