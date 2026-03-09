using NodaTime;

namespace Shopkeeper.Api.Contracts;

public sealed record RegisterOwnerRequest(
    string FullName,
    string? Email,
    string? Phone,
    string Password,
    string ShopName,
    bool VatEnabled,
    decimal VatRate);

public sealed record LoginRequest(
    string Login,
    string Password,
    Guid? ShopId);

public sealed record RefreshRequest(string RefreshToken);

public sealed record GoogleMobileAuthRequest(string IdToken, Guid? ShopId);

public sealed record MagicLinkRequest(string Email, Guid? ShopId);

public sealed record MagicLinkVerifyRequest(string Token, Guid? ShopId);

public sealed record MagicLinkRequestResponse(
    Guid RequestId,
    Instant ExpiresAtUtc,
    string Message,
    string? DebugToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    Instant AccessTokenExpiresAtUtc,
    Guid ShopId,
    string Role);
