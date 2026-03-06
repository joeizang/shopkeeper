using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Identity;

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

    public ICollection<ShopMembership> Memberships { get; set; } = [];
}

public class UserAccount : IdentityUser<Guid>
{
    public override Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PreferredLanguage { get; set; } = "en";
    public string? Timezone { get; set; } = "UTC";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ShopMembership> Memberships { get; set; } = [];
    public ICollection<AuthIdentity> AuthIdentities { get; set; } = [];
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
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public UserAccount UserAccount { get; set; } = default!;
    public ShopMembership ShopMembership { get; set; } = default!;
}

public sealed class AuthIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderSubject { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAtUtc { get; set; } = DateTime.UtcNow;

    public UserAccount UserAccount { get; set; } = default!;
}

public sealed class MagicLinkChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserAccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid? RequestedShopId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string? RequestIp { get; set; }
    public string? UserAgent { get; set; }

    public UserAccount? UserAccount { get; set; }
}

public sealed class EmailOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}
