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

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    Guid ShopId,
    string Role);
