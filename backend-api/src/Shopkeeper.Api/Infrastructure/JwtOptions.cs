namespace Shopkeeper.Api.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "shopkeeper-api";
    public string Audience { get; init; } = "shopkeeper-mobile";
    public string SigningKey { get; init; } = "change-this-key-in-production-32-chars-minimum";
    public int AccessTokenMinutes { get; init; } = 60;
    public int RefreshTokenDays { get; init; } = 30;
}
