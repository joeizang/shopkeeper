using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/register-owner", RegisterOwner);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);

        return app;
    }

    private static async Task<IResult> RegisterOwner(
        [FromBody] RegisterOwnerRequest request,
        ShopkeeperDbContext db,
        PasswordHasher hasher,
        AuthTokenService tokenService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Phone))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["login"] = ["Provide at least email or phone."]
            });
        }

        var emailExists = !string.IsNullOrWhiteSpace(request.Email)
            && await db.Users.AnyAsync(x => x.Email == request.Email, ct);
        var phoneExists = !string.IsNullOrWhiteSpace(request.Phone)
            && await db.Users.AnyAsync(x => x.Phone == request.Phone, ct);

        if (emailExists || phoneExists)
        {
            return Results.Conflict(new { message = "User already exists." });
        }

        var user = new UserAccount
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = hasher.HashPassword(request.Password)
        };

        var shopCodeSeed = request.ShopName.Replace(" ", string.Empty).ToUpperInvariant();
        var suffix = Random.Shared.Next(1000, 9999);
        var shop = new Shop
        {
            Name = request.ShopName,
            Code = $"{shopCodeSeed[..Math.Min(shopCodeSeed.Length, 6)]}{suffix}",
            VatEnabled = request.VatEnabled,
            VatRate = request.VatRate <= 0 ? 0.075m : request.VatRate
        };

        var membership = new ShopMembership
        {
            Shop = shop,
            UserAccount = user,
            Role = MembershipRole.Owner,
            IsActive = true
        };

        db.Users.Add(user);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(membership);

        var refreshTokenTuple = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccount = user,
            ShopMembership = membership,
            TokenHash = refreshTokenTuple.hash,
            ExpiresAtUtc = refreshTokenTuple.expiresAtUtc
        });

        await db.SaveChangesAsync(ct);

        var accessToken = tokenService.GenerateAccessToken(user, membership);

        return Results.Ok(new AuthResponse(
            accessToken,
            refreshTokenTuple.token,
            DateTime.UtcNow.AddMinutes(60),
            shop.Id,
            membership.Role.ToString()));
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        ShopkeeperDbContext db,
        PasswordHasher hasher,
        AuthTokenService tokenService,
        CancellationToken ct)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(x => x.Email == request.Login || x.Phone == request.Login, ct);

        if (user is null || !hasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Results.Unauthorized();
        }

        var membershipsQuery = db.ShopMemberships
            .Where(x => x.UserAccountId == user.Id && x.IsActive);

        var membership = request.ShopId.HasValue
            ? await membershipsQuery.FirstOrDefaultAsync(x => x.ShopId == request.ShopId.Value, ct)
            : await membershipsQuery.FirstOrDefaultAsync(ct);

        if (membership is null)
        {
            return Results.Forbid();
        }

        var refreshTokenTuple = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccountId = user.Id,
            ShopMembershipId = membership.Id,
            TokenHash = refreshTokenTuple.hash,
            ExpiresAtUtc = refreshTokenTuple.expiresAtUtc
        });

        await db.SaveChangesAsync(ct);

        var accessToken = tokenService.GenerateAccessToken(user, membership);

        return Results.Ok(new AuthResponse(
            accessToken,
            refreshTokenTuple.token,
            DateTime.UtcNow.AddMinutes(60),
            membership.ShopId,
            membership.Role.ToString()));
    }

    private static async Task<IResult> Refresh(
        [FromBody] RefreshRequest request,
        ShopkeeperDbContext db,
        AuthTokenService tokenService,
        CancellationToken ct)
    {
        var tokenHash = tokenService.ComputeSha256(request.RefreshToken);
        var refreshToken = await db.RefreshTokens
            .Include(x => x.UserAccount)
            .Include(x => x.ShopMembership)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

        if (refreshToken is null || refreshToken.RevokedAtUtc.HasValue || refreshToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Results.Unauthorized();
        }

        refreshToken.RevokedAtUtc = DateTime.UtcNow;

        var newRefresh = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccountId = refreshToken.UserAccountId,
            ShopMembershipId = refreshToken.ShopMembershipId,
            TokenHash = newRefresh.hash,
            ExpiresAtUtc = newRefresh.expiresAtUtc
        });

        await db.SaveChangesAsync(ct);

        var accessToken = tokenService.GenerateAccessToken(refreshToken.UserAccount, refreshToken.ShopMembership);

        return Results.Ok(new AuthResponse(
            accessToken,
            newRefresh.token,
            DateTime.UtcNow.AddMinutes(60),
            refreshToken.ShopMembership.ShopId,
            refreshToken.ShopMembership.Role.ToString()));
    }
}
