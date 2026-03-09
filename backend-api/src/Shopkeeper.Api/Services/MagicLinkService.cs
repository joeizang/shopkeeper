using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NodaTime;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class MagicLinkService
{
    private readonly MagicLinkOptions _options;

    public MagicLinkService(IOptions<MagicLinkOptions> options)
    {
        _options = options.Value;
    }

    public (string token, string hash, Instant expiresAtUtc) CreateToken()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = ComputeSha256(token);
        var expiresAt = SystemClock.Instance.GetCurrentInstant() + Duration.FromMinutes(Math.Max(5, _options.ExpiryMinutes));
        return (token, hash, expiresAt);
    }

    public string BuildMagicLink(string token)
    {
        var separator = _options.AppLinkBaseUrl.Contains('?') ? "&" : "?";
        return $"{_options.AppLinkBaseUrl}{separator}token={Uri.EscapeDataString(token)}";
    }

    public string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
