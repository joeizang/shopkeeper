using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .RequireRateLimiting("auth");

        group.MapPost("/register-owner", RegisterOwner);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/google/mobile", GoogleMobile);
        group.MapPost("/magic-link/request", RequestMagicLink);
        group.MapPost("/magic-link/verify", VerifyMagicLink);

        return app;
    }

    private static async Task<IResult> RegisterOwner(
        [FromBody] RegisterOwnerRequest request,
        ShopkeeperDbContext db,
        UserManager<UserAccount> userManager,
        AuthTokenService tokenService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Phone))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["login"] = ["Provide at least email or phone."]
            });
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var normalizedPhone = NormalizePhone(request.Phone);

        var emailExists = !string.IsNullOrWhiteSpace(normalizedEmail)
            && await db.Users.AnyAsync(x => x.Email == normalizedEmail, ct);
        var phoneExists = !string.IsNullOrWhiteSpace(normalizedPhone)
            && await db.Users.AnyAsync(x => x.PhoneNumber == normalizedPhone, ct);

        if (emailExists || phoneExists)
        {
            return Results.Conflict(new { message = "An account with those credentials already exists." });
        }

        var user = new UserAccount
        {
            FullName = request.FullName,
            Email = normalizedEmail,
            UserName = BuildUserName(normalizedEmail, normalizedPhone),
            PhoneNumber = normalizedPhone,
            EmailConfirmed = false
        };

        var create = await userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
        {
            return Results.ValidationProblem(create.Errors
                .GroupBy(x => x.Code)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Description).ToArray()));
        }

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
            UserAccountId = user.Id,
            Role = MembershipRole.Owner,
            IsActive = true
        };

        db.Shops.Add(shop);
        db.ShopMemberships.Add(membership);

        await UpsertAuthIdentityAsync(db, user.Id, "password", normalizedEmail ?? normalizedPhone ?? user.Id.ToString("N"), normalizedEmail, false, ct);

        var device = ReadDevice(httpContext);
        var refreshTokenTuple = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccountId = user.Id,
            ShopMembership = membership,
            TokenHash = refreshTokenTuple.hash,
            ExpiresAtUtc = refreshTokenTuple.expiresAtUtc,
            DeviceId = device.deviceId,
            DeviceName = device.deviceName,
            LastSeenAtUtc = SystemClock.Instance.GetCurrentInstant()
        });

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = shop.Id,
            UserAccountId = user.Id,
            Action = "auth.register-owner",
            EntityName = nameof(UserAccount),
            EntityId = user.Id,
            PayloadJson = "{}"
        });

        await db.SaveChangesAsync(ct);

        return OkAuth(tokenService, user, membership, refreshTokenTuple.token);
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        ShopkeeperDbContext db,
        UserManager<UserAccount> userManager,
        AuthTokenService tokenService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        const string genericFailure = "Invalid login request.";
        var user = await FindUserByLoginAsync(db, request.Login, ct);
        if (user is not null && await userManager.IsLockedOutAsync(user))
        {
            return Results.Unauthorized();
        }

        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            if (user is not null)
            {
                await userManager.AccessFailedAsync(user);
            }

            return Results.Json(new { message = genericFailure }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var membership = await ResolveMembershipAsync(db, user.Id, request.ShopId, ct);
        if (membership is null)
        {
            await userManager.AccessFailedAsync(user);
            return Results.Json(new { message = genericFailure }, statusCode: StatusCodes.Status401Unauthorized);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var device = ReadDevice(httpContext);
        var refreshTokenTuple = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccountId = user.Id,
            ShopMembershipId = membership.Id,
            TokenHash = refreshTokenTuple.hash,
            ExpiresAtUtc = refreshTokenTuple.expiresAtUtc,
            DeviceId = device.deviceId,
            DeviceName = device.deviceName,
            LastSeenAtUtc = SystemClock.Instance.GetCurrentInstant()
        });

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = membership.ShopId,
            UserAccountId = user.Id,
            Action = "auth.login.password",
            EntityName = nameof(UserAccount),
            EntityId = user.Id,
            PayloadJson = "{}"
        });

        await db.SaveChangesAsync(ct);

        return OkAuth(tokenService, user, membership, refreshTokenTuple.token);
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

        if (refreshToken is null
            || refreshToken.RevokedAtUtc.HasValue
            || refreshToken.ExpiresAtUtc <= SystemClock.Instance.GetCurrentInstant()
            || !refreshToken.ShopMembership.IsActive)
        {
            return Results.Unauthorized();
        }

        refreshToken.RevokedAtUtc = SystemClock.Instance.GetCurrentInstant();

        var newRefresh = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccountId = refreshToken.UserAccountId,
            ShopMembershipId = refreshToken.ShopMembershipId,
            TokenHash = newRefresh.hash,
            ExpiresAtUtc = newRefresh.expiresAtUtc,
            DeviceId = refreshToken.DeviceId,
            DeviceName = refreshToken.DeviceName,
            LastSeenAtUtc = SystemClock.Instance.GetCurrentInstant()
        });

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = refreshToken.ShopMembership.ShopId,
            UserAccountId = refreshToken.UserAccountId,
            Action = "auth.refresh",
            EntityName = nameof(RefreshToken),
            EntityId = refreshToken.Id,
            PayloadJson = "{}"
        });

        await db.SaveChangesAsync(ct);

        return OkAuth(tokenService, refreshToken.UserAccount, refreshToken.ShopMembership, newRefresh.token);
    }

    private static async Task<IResult> GoogleMobile(
        [FromBody] GoogleMobileAuthRequest request,
        ShopkeeperDbContext db,
        IGoogleTokenValidator tokenValidator,
        AuthTokenService tokenService,
        CancellationToken ct)
    {
        var principal = await tokenValidator.ValidateAsync(request.IdToken, ct);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var identity = await db.AuthIdentities
            .Include(x => x.UserAccount)
            .FirstOrDefaultAsync(x => x.Provider == "google" && x.ProviderSubject == principal.Subject, ct);

        var user = identity?.UserAccount;
        var normalizedEmail = NormalizeEmail(principal.Email);

        if (user is null && !string.IsNullOrWhiteSpace(normalizedEmail))
        {
            user = await db.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        }

        if (user is null)
        {
            user = new UserAccount
            {
                FullName = principal.FullName?.Trim() ?? "Google User",
                Email = normalizedEmail,
                UserName = BuildUserName(normalizedEmail, null),
                EmailConfirmed = principal.EmailVerified
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        await UpsertAuthIdentityAsync(db, user.Id, "google", principal.Subject, normalizedEmail, principal.EmailVerified, ct);

        var membership = await ResolveMembershipAsync(db, user.Id, request.ShopId, ct);
        if (membership is null)
        {
            return Results.Unauthorized();
        }

        var refreshTokenTuple = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccountId = user.Id,
            ShopMembershipId = membership.Id,
            TokenHash = refreshTokenTuple.hash,
            ExpiresAtUtc = refreshTokenTuple.expiresAtUtc,
            LastSeenAtUtc = SystemClock.Instance.GetCurrentInstant()
        });

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = membership.ShopId,
            UserAccountId = user.Id,
            Action = "auth.login.google",
            EntityName = nameof(UserAccount),
            EntityId = user.Id,
            PayloadJson = "{}"
        });

        await db.SaveChangesAsync(ct);

        return OkAuth(tokenService, user, membership, refreshTokenTuple.token);
    }

    private static async Task<IResult> RequestMagicLink(
        [FromBody] MagicLinkRequest request,
        ShopkeeperDbContext db,
        MagicLinkService magicLinkService,
        IOptions<MagicLinkOptions> options,
        IWebHostEnvironment environment,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is required."]
            });
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var recentRequests = await db.MagicLinkChallenges.CountAsync(
            x => x.Email == email && x.RequestedAtUtc >= now - Duration.FromMinutes(1), ct);
        if (recentRequests >= Math.Max(1, options.Value.MaxRequestsPerMinutePerEmail))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        var targetShop = request.ShopId;
        var membership = user is null ? null : await ResolveMembershipAsync(db, user.Id, targetShop, ct);

        if (user is null || membership is null)
        {
            return Results.Accepted(
                "/api/v1/auth/magic-link/verify",
                new MagicLinkRequestResponse(Guid.Empty, now + Duration.FromMinutes(options.Value.ExpiryMinutes), "If this email is registered, a sign-in link has been queued.", null));
        }

        var token = magicLinkService.CreateToken();
        var challenge = new MagicLinkChallenge
        {
            UserAccountId = user.Id,
            Email = email,
            RequestedShopId = request.ShopId,
            TokenHash = token.hash,
            ExpiresAtUtc = token.expiresAtUtc,
            RequestIp = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString()
        };
        db.MagicLinkChallenges.Add(challenge);

        var link = magicLinkService.BuildMagicLink(token.token);
        db.EmailOutboxMessages.Add(new EmailOutboxMessage
        {
            ToEmail = email,
            Subject = "Your Shopkeeper sign-in link",
            Body = $"Open this link to sign in: {link}",
            Status = "Pending"
        });

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = membership.ShopId,
            UserAccountId = user.Id,
            Action = "auth.magic-link.request",
            EntityName = nameof(MagicLinkChallenge),
            EntityId = challenge.Id,
            PayloadJson = "{}"
        });

        await db.SaveChangesAsync(ct);

        var debugToken = environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? token.token
            : null;

        return Results.Accepted(
            "/api/v1/auth/magic-link/verify",
            new MagicLinkRequestResponse(
                challenge.Id,
                challenge.ExpiresAtUtc,
                "If this email is registered, a sign-in link has been queued.",
                debugToken));
    }

    private static async Task<IResult> VerifyMagicLink(
        [FromBody] MagicLinkVerifyRequest request,
        ShopkeeperDbContext db,
        AuthTokenService tokenService,
        MagicLinkService magicLinkService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["token"] = ["Token is required."]
            });
        }

        var hash = magicLinkService.ComputeSha256(request.Token);
        var now = SystemClock.Instance.GetCurrentInstant();
        var challenge = await db.MagicLinkChallenges
            .Include(x => x.UserAccount)
            .FirstOrDefaultAsync(x =>
                x.TokenHash == hash
                && x.ConsumedAtUtc == null
                && x.ExpiresAtUtc > now, ct);

        if (challenge is null)
        {
            return Results.Unauthorized();
        }

        var user = challenge.UserAccount
            ?? await db.Users.FirstOrDefaultAsync(x => x.Email == challenge.Email, ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var targetShop = request.ShopId ?? challenge.RequestedShopId;
        var membership = await ResolveMembershipAsync(db, user.Id, targetShop, ct);
        if (membership is null)
        {
            return Results.Unauthorized();
        }

        challenge.ConsumedAtUtc = now;
        user.EmailConfirmed = true;
        await UpsertAuthIdentityAsync(db, user.Id, "magic_link", challenge.Email, challenge.Email, true, ct);

        var refresh = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserAccountId = user.Id,
            ShopMembershipId = membership.Id,
            TokenHash = refresh.hash,
            ExpiresAtUtc = refresh.expiresAtUtc,
            LastSeenAtUtc = now
        });

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = membership.ShopId,
            UserAccountId = user.Id,
            Action = "auth.magic-link.verify",
            EntityName = nameof(MagicLinkChallenge),
            EntityId = challenge.Id,
            PayloadJson = "{}"
        });

        await db.SaveChangesAsync(ct);

        return OkAuth(tokenService, user, membership, refresh.token);
    }

    private static IResult OkAuth(
        AuthTokenService tokenService,
        UserAccount user,
        ShopMembership membership,
        string refreshToken)
    {
        var accessToken = tokenService.GenerateAccessToken(user, membership);
        return Results.Ok(new AuthResponse(
            accessToken,
            refreshToken,
            tokenService.GetAccessTokenExpiryUtc(),
            membership.ShopId,
            membership.Role.ToString()));
    }

    private static async Task<UserAccount?> FindUserByLoginAsync(ShopkeeperDbContext db, string login, CancellationToken ct)
    {
        var normalized = login.Trim();
        var normalizedEmail = NormalizeEmail(normalized);

        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return await db.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        }

        var phone = NormalizePhone(normalized);
        return await db.Users.FirstOrDefaultAsync(x => x.PhoneNumber == phone, ct);
    }

    private static async Task<ShopMembership?> ResolveMembershipAsync(
        ShopkeeperDbContext db,
        Guid userId,
        Guid? shopId,
        CancellationToken ct)
    {
        var membershipsQuery = db.ShopMemberships
            .Where(x => x.UserAccountId == userId && x.IsActive);

        return shopId.HasValue
            ? await membershipsQuery.FirstOrDefaultAsync(x => x.ShopId == shopId.Value, ct)
            : await membershipsQuery.FirstOrDefaultAsync(ct);
    }

    private static async Task UpsertAuthIdentityAsync(
        ShopkeeperDbContext db,
        Guid userId,
        string provider,
        string providerSubject,
        string? email,
        bool emailVerified,
        CancellationToken ct)
    {
        var identity = await db.AuthIdentities
            .FirstOrDefaultAsync(x => x.Provider == provider && x.ProviderSubject == providerSubject, ct);

        if (identity is null)
        {
            db.AuthIdentities.Add(new AuthIdentity
            {
                UserAccountId = userId,
                Provider = provider,
                ProviderSubject = providerSubject,
                Email = email,
                EmailVerified = emailVerified,
                LastUsedAtUtc = SystemClock.Instance.GetCurrentInstant()
            });
            return;
        }

        identity.UserAccountId = userId;
        identity.Email = email;
        identity.EmailVerified = emailVerified;
        identity.LastUsedAtUtc = SystemClock.Instance.GetCurrentInstant();
    }

    private static (string? deviceId, string? deviceName) ReadDevice(HttpContext httpContext)
    {
        var deviceId = httpContext.Request.Headers["X-Device-Id"].ToString();
        var deviceName = httpContext.Request.Headers["X-Device-Name"].ToString();
        return (string.IsNullOrWhiteSpace(deviceId) ? null : deviceId, string.IsNullOrWhiteSpace(deviceName) ? null : deviceName);
    }

    private static string BuildUserName(string? email, string? phone)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            return $"phone_{phone}";
        }

        return $"user_{Guid.NewGuid():N}";
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim().ToLowerInvariant();
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        return phone.Trim();
    }

    private static string GuessFullNameFromEmail(string email)
    {
        var local = email.Split('@').FirstOrDefault()?.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        if (string.IsNullOrWhiteSpace(local))
        {
            return "Shopkeeper User";
        }

        return string.Join(' ', local
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    }
}
