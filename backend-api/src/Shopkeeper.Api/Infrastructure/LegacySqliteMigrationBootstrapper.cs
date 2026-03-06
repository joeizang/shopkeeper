using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Data;

namespace Shopkeeper.Api.Infrastructure;

internal static class LegacySqliteMigrationBootstrapper
{
    private const string InitialCreateMigrationId = "20260304161941_InitialCreate";
    private const string AuthRefactorMigrationId = "20260305150934_AuthAndAccountRefactor";
    private const string EfProductVersion = "10.0.0";

    private static readonly HashSet<string> InitialSchemaTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuditLogs",
        "InventoryItems",
        "Sales",
        "Shops",
        "Users"
    };

    private static readonly HashSet<string> AuthSchemaTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuthIdentities",
        "EmailOutboxMessages",
        "MagicLinkChallenges",
        "UserClaims",
        "UserLogins",
        "UserTokens"
    };

    public static async Task BootstrapAsync(ShopkeeperDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite())
        {
            return;
        }

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            var tableNames = await ReadTableNamesAsync(connection, ct);
            if (tableNames.Count == 0)
            {
                return;
            }

            var historyExists = tableNames.Contains("__EFMigrationsHistory");
            var appliedMigrations = historyExists
                ? await ReadAppliedMigrationsAsync(connection, ct)
                : [];

            if (appliedMigrations.Count > 0)
            {
                return;
            }

            var hasInitialSchema = InitialSchemaTables.All(tableNames.Contains);
            var hasAuthSchema = AuthSchemaTables.All(tableNames.Contains);
            if (!hasInitialSchema)
            {
                return;
            }

            await EnsureHistoryTableAsync(connection, ct);
            await InsertHistoryRowAsync(connection, InitialCreateMigrationId, ct);

            if (hasAuthSchema)
            {
                await InsertHistoryRowAsync(connection, AuthRefactorMigrationId, ct);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(SqliteConnection connection, CancellationToken ct)
    {
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%';
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    private static async Task<HashSet<string>> ReadAppliedMigrationsAsync(SqliteConnection connection, CancellationToken ct)
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MigrationId
            FROM __EFMigrationsHistory;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private static async Task EnsureHistoryTableAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertHistoryRowAsync(SqliteConnection connection, string migrationId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ($migrationId, $productVersion);
            """;
        command.Parameters.AddWithValue("$migrationId", migrationId);
        command.Parameters.AddWithValue("$productVersion", EfProductVersion);
        await command.ExecuteNonQueryAsync(ct);
    }
}
