using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shopkeeper.Api.Data;

public sealed class ShopkeeperDbContextFactory : IDesignTimeDbContextFactory<ShopkeeperDbContext>
{
    public ShopkeeperDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ShopkeeperDbContext>();
        optionsBuilder.UseSqlite("Data Source=shopkeeper.db");
        return new ShopkeeperDbContext(optionsBuilder.Options);
    }
}
