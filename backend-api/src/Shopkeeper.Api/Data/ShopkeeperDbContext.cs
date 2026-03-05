using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Data;

public sealed class ShopkeeperDbContext : IdentityUserContext<UserAccount, Guid>
{
    public ShopkeeperDbContext(DbContextOptions<ShopkeeperDbContext> options)
        : base(options)
    {
    }

    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<ShopMembership> ShopMemberships => Set<ShopMembership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuthIdentity> AuthIdentities => Set<AuthIdentity>();
    public DbSet<MagicLinkChallenge> MagicLinkChallenges => Set<MagicLinkChallenge>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<ItemPhoto> ItemPhotos => Set<ItemPhoto>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<CreditAccount> CreditAccounts => Set<CreditAccount>();
    public DbSet<CreditRepayment> CreditRepayments => Set<CreditRepayment>();
    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();
    public DbSet<DeviceCheckpoint> DeviceCheckpoints => Set<DeviceCheckpoint>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserAccount>().ToTable("Users");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        modelBuilder.Entity<Shop>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<ShopMembership>()
            .HasOne(x => x.UserAccount)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.UserAccountId);

        modelBuilder.Entity<ShopMembership>()
            .HasOne(x => x.Shop)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.ShopId);

        modelBuilder.Entity<UserAccount>()
            .HasIndex(x => x.Email);

        modelBuilder.Entity<UserAccount>()
            .HasIndex(x => x.PhoneNumber);

        modelBuilder.Entity<AuthIdentity>()
            .HasOne(x => x.UserAccount)
            .WithMany(x => x.AuthIdentities)
            .HasForeignKey(x => x.UserAccountId);

        modelBuilder.Entity<AuthIdentity>()
            .HasIndex(x => new { x.Provider, x.ProviderSubject })
            .IsUnique();

        modelBuilder.Entity<MagicLinkChallenge>()
            .HasOne(x => x.UserAccount)
            .WithMany()
            .HasForeignKey(x => x.UserAccountId);

        modelBuilder.Entity<MagicLinkChallenge>()
            .HasIndex(x => x.TokenHash)
            .IsUnique();

        modelBuilder.Entity<InventoryItem>()
            .HasIndex(x => new { x.TenantId, x.SerialNumber })
            .IsUnique();

        modelBuilder.Entity<ItemPhoto>()
            .HasOne(x => x.InventoryItem)
            .WithMany(x => x.Photos)
            .HasForeignKey(x => x.InventoryItemId);

        modelBuilder.Entity<StockAdjustment>()
            .HasOne(x => x.InventoryItem)
            .WithMany()
            .HasForeignKey(x => x.InventoryItemId);

        modelBuilder.Entity<Sale>()
            .HasIndex(x => new { x.TenantId, x.SaleNumber })
            .IsUnique();

        modelBuilder.Entity<SaleLine>()
            .HasOne(x => x.Sale)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.SaleId);

        modelBuilder.Entity<SalePayment>()
            .HasOne(x => x.Sale)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.SaleId);

        modelBuilder.Entity<CreditAccount>()
            .HasOne(x => x.Sale)
            .WithOne(x => x.CreditAccount)
            .HasForeignKey<CreditAccount>(x => x.SaleId);

        modelBuilder.Entity<CreditRepayment>()
            .HasOne(x => x.CreditAccount)
            .WithMany(x => x.Repayments)
            .HasForeignKey(x => x.CreditAccountId);

        modelBuilder.Entity<CreditRepayment>()
            .HasOne(x => x.SalePayment)
            .WithMany()
            .HasForeignKey(x => x.SalePaymentId);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(x => x.UserAccount)
            .WithMany()
            .HasForeignKey(x => x.UserAccountId);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(x => x.ShopMembership)
            .WithMany()
            .HasForeignKey(x => x.ShopMembershipId);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.TokenHash)
            .IsUnique();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IMutableTenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IMutableTenantEntity.RowVersion))
                    .IsConcurrencyToken();
            }
        }
    }

    public override int SaveChanges()
    {
        ApplyMutableEntityUpdates();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyMutableEntityUpdates();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyMutableEntityUpdates()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IMutableTenantEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.RowVersion = BitConverter.GetBytes(now.Ticks);
            }
        }
    }
}
