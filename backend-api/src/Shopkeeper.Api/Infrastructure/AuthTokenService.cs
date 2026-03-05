using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Infrastructure;

public sealed class AuthTokenService
{
    private readonly JwtOptions _options;

    public AuthTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateAccessToken(UserAccount user, ShopMembership membership)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(CustomClaimTypes.TenantId, membership.ShopId.ToString()),
            new(CustomClaimTypes.MembershipId, membership.Id.ToString()),
            new(CustomClaimTypes.Role, membership.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTime GetAccessTokenExpiryUtc()
    {
        return DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
    }

    public (string token, string hash, DateTime expiresAtUtc) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes);
        var hash = ComputeSha256(token);
        var expiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);
        return (token, hash, expiresAt);
    }

    public string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
