namespace Shopkeeper.Api.Infrastructure;

public sealed class E2ETestOptions
{
    public const string SectionName = "E2E";

    public string AdminToken { get; set; } = string.Empty;
}
