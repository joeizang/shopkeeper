using NodaTime;

namespace Shopkeeper.Api.Contracts;

public sealed record AccountProfileResponse(
    Guid UserId,
    string FullName,
    string? Email,
    string? Phone,
    string? AvatarUrl,
    string? PreferredLanguage,
    string? Timezone,
    Instant CreatedAtUtc);

public sealed record UpdateAccountProfileRequest(
    string FullName,
    string? Phone,
    string? AvatarUrl,
    string? PreferredLanguage,
    string? Timezone);

public sealed record SessionView(
    Guid SessionId,
    Guid ShopId,
    string Role,
    string? DeviceId,
    string? DeviceName,
    Instant CreatedAtUtc,
    Instant ExpiresAtUtc,
    Instant? LastSeenAtUtc,
    bool IsRevoked);

public sealed record LinkedIdentityView(
    string Provider,
    string ProviderSubject,
    string? Email,
    bool EmailVerified,
    Instant CreatedAtUtc,
    Instant LastUsedAtUtc);
