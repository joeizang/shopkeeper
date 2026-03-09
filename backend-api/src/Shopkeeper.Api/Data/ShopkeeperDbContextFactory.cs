using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shopkeeper.Api.Data;

public sealed class ShopkeeperDbContextFactory : IDesignTimeDbContextFactory<ShopkeeperDbContext>
{
    public ShopkeeperDbContext CreateDbContext(string[] args)
    {
        // Reads CONNECTIONSTRINGS__DEFAULT from environment, or falls back to a local dev default.
        var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULT")
            ?? "Host=192.168.0.5;Port=5432;Database=shopkeeper;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ShopkeeperDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.UseNodaTime());
        return new ShopkeeperDbContext(optionsBuilder.Options);
    }
}
