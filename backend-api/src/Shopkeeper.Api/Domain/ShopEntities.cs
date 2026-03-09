using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Identity;
using NodaTime;

namespace Shopkeeper.Api.Domain;

public sealed class Shop : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool VatEnabled { get; set; } = true;
    public decimal VatRate { get; set; } = 0.075m;
    public decimal DefaultDiscountPercent { get; set; }
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant UpdatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
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
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();

    public ICollection<ShopMembership> Memberships { get; set; } = [];
    public ICollection<AuthIdentity> AuthIdentities { get; set; } = [];
}

public sealed class ShopMembership : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Guid UserAccountId { get; set; }
    public MembershipRole Role { get; set; } = MembershipRole.Salesperson;
    public bool IsActive { get; set; } = true;
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant UpdatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
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
    public Instant ExpiresAtUtc { get; set; }
    public Instant? RevokedAtUtc { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public Instant? LastSeenAtUtc { get; set; }
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();

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
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant LastUsedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();

    public UserAccount UserAccount { get; set; } = default!;
}

public sealed class MagicLinkChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserAccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid? RequestedShopId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Instant ExpiresAtUtc { get; set; }
    public Instant? ConsumedAtUtc { get; set; }
    public Instant RequestedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
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
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}
