using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Data;

namespace Shopkeeper.Api.Infrastructure;

internal static class LegacySqliteMigrationBootstrapper
{
    private const string EfProductVersion = "10.0.0";

    private static readonly HashSet<string> CurrentSchemaTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuditLogs",
        "AuthIdentities",
        "CreditAccounts",
        "CreditRepayments",
        "DeviceCheckpoints",
        "EmailOutboxMessages",
        "Expenses",
        "IdempotencyRecords",
        "InventoryItems",
        "ItemPhotos",
        "MagicLinkChallenges",
        "RefreshTokens",
        "ReportFiles",
        "ReportJobs",
        "SaleLines",
        "SalePayments",
        "Sales",
        "ShopMemberships",
        "Shops",
        "StockAdjustments",
        "SyncChanges",
        "UserClaims",
        "UserLogins",
        "Users",
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

            if (!CurrentSchemaTables.All(tableNames.Contains))
            {
                return;
            }

            var latestMigrationId = db.Database.GetMigrations().LastOrDefault();
            if (string.IsNullOrWhiteSpace(latestMigrationId))
            {
                return;
            }

            await EnsureHistoryTableAsync(connection, ct);
            await InsertHistoryRowAsync(connection, latestMigrationId, ct);
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
