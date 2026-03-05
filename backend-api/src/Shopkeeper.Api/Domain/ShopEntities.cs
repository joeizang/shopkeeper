namespace Shopkeeper.Api.Domain;

public sealed class Shop : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool VatEnabled { get; set; } = true;
    public decimal VatRate { get; set; } = 0.075m;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Guid TenantId
    {
        get => Id;
        set => Id = value;
    }

    public ICollection<ShopMembership> Memberships { get; set; } = new List<ShopMembership>();
}

public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ShopMembership> Memberships { get; set; } = new List<ShopMembership>();
}

public sealed class ShopMembership : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Guid UserAccountId { get; set; }
    public MembershipRole Role { get; set; } = MembershipRole.Staff;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Guid TenantId
    {
        get => ShopId;
        set => ShopId = value;
    }

    public Shop Shop { get; set; } = default!;
    public UserAccount UserAccount { get; set; } = default!;
}

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public Guid ShopMembershipId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public UserAccount UserAccount { get; set; } = default!;
    public ShopMembership ShopMembership { get; set; } = default!;
}
