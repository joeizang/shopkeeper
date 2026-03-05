using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public interface IGoogleTokenValidator
{
    Task<GooglePrincipal?> ValidateAsync(string idToken, CancellationToken ct);
}

public sealed record GooglePrincipal(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? FullName);

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly GoogleAuthOptions _options;

    public GoogleTokenValidator(IOptions<GoogleAuthOptions> options)
    {
        _options = options.Value;
    }

    public async Task<GooglePrincipal?> ValidateAsync(string idToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = _options.AllowedAudiences
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }
        catch
        {
            return null;
        }

        return new GooglePrincipal(
            payload.Subject,
            payload.Email,
            payload.EmailVerified,
            payload.Name);
    }
}
