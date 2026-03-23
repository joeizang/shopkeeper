using dotenv.net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shopkeeper.Api.Data;

public sealed class ShopkeeperDbContextFactory : IDesignTimeDbContextFactory<ShopkeeperDbContext>
{
    public ShopkeeperDbContext CreateDbContext(string[] args)
    {
        DotEnv.Load(options: new DotEnvOptions(envFilePaths: ["./.env", "./.env.local"], ignoreExceptions: true, overwriteExistingVars: false));

        var connectionString = Environment.GetEnvironmentVariable("DbConnectionString")
            ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULT")
            ?? throw new InvalidOperationException("DbConnectionString must be configured for design-time operations.");

        var optionsBuilder = new DbContextOptionsBuilder<ShopkeeperDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.UseNodaTime());
        return new ShopkeeperDbContext(optionsBuilder.Options);
    }
}
