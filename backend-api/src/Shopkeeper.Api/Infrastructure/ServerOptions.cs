namespace Shopkeeper.Api.Infrastructure;

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string ListenHost { get; init; } = "127.0.0.1";
    public int ListenPort { get; init; } = 5000;
    public bool TrustAllForwardedHeaders { get; init; } = false;
    public string[] TrustedProxies { get; init; } = [];
}
