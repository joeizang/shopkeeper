namespace Shopkeeper.Api.Infrastructure;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string[] AllowedAudiences { get; init; } = Array.Empty<string>();
}
