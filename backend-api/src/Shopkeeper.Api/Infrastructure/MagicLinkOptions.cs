namespace Shopkeeper.Api.Infrastructure;

public sealed class MagicLinkOptions
{
    public const string SectionName = "MagicLink";

    public int ExpiryMinutes { get; init; } = 30;
    public int MaxRequestsPerMinutePerEmail { get; init; } = 3;
    public string AppLinkBaseUrl { get; init; } = "shopkeeper://auth/magic-link";
}
