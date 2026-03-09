using Microsoft.Data.Sqlite;

namespace Shopkeeper.Api.Infrastructure;

internal static class SqliteConnectionStringResolver
{
    public static string Resolve(string connectionString, string basePath)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || Path.IsPathRooted(builder.DataSource) || builder.DataSource == ":memory:")
        {
            return builder.ToString();
        }

        builder.DataSource = Path.GetFullPath(builder.DataSource, basePath);
        return builder.ToString();
    }
}
