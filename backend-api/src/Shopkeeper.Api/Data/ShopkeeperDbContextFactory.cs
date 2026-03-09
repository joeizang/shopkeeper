using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Data;

public sealed class ShopkeeperDbContextFactory : IDesignTimeDbContextFactory<ShopkeeperDbContext>
{
    public ShopkeeperDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ShopkeeperDbContext>();
        var contentRoot = ResolveContentRoot();
        var connectionString = SqliteConnectionStringResolver.Resolve("Data Source=shopkeeper.db", contentRoot);
        optionsBuilder.UseSqlite(connectionString);
        return new ShopkeeperDbContext(optionsBuilder.Options);
    }

    private static string ResolveContentRoot()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            if (File.Exists(Path.Combine(current, "Shopkeeper.Api.csproj")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
