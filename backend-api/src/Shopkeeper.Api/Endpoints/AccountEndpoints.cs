using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/account").RequireAuthorization();

        group.MapGet("/me", GetMe);
        group.MapPatch("/me", UpdateMe);
        group.MapGet("/sessions", GetSessions);
        group.MapPost("/sessions/revoke-all", RevokeAllSessions);
        group.MapPost("/sessions/{id:guid}/revoke", RevokeSession);
        group.MapGet("/linked-identities", GetLinkedIdentities);

        return app;
    }

    private static async Task<IResult> GetMe(
        AccountReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = tenant.GetUserId(httpContext.User);
        if (!userId.HasValue)
        {
            return Results.Unauthorized();
        }

        var cached = await reads.GetProfileAsync(userId.Value, ct);
        return cached.Value is null
            ? Results.NotFound()
            : HttpCacheResults.OkOrNotModified(httpContext, cached!);
    }

    private static async Task<IResult> UpdateMe(
        [FromBody] UpdateAccountProfileRequest request,
        ShopkeeperDbContext db,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = tenant.GetUserId(httpContext.User);
        if (!userId.HasValue)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId.Value, ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            var trimmedUrl = request.AvatarUrl.Trim();
            if (trimmedUrl.Length > 2048 ||
                !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var parsedUri) ||
                (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["avatarUrl"] = ["Avatar URL must be a valid http or https URL, max 2048 characters."]
                });
            }
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        user.PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage) ? "en" : request.PreferredLanguage.Trim();
        user.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim();

        db.AuditLogs.Add(new Domain.AuditLog
        {
            TenantId = tenant.GetTenantId(httpContext.User) ?? Guid.Empty,
            UserAccountId = user.Id,
            Action = "account.profile.update",
            EntityName = nameof(Domain.UserAccount),
            EntityId = user.Id,
            PayloadJson = "{}"
        });

        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.UserAccount(userId.Value)], ct);

        return Results.Ok(new AccountProfileResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.AvatarUrl,
            user.PreferredLanguage,
            user.Timezone,
            user.CreatedAtUtc));
    }

    private static async Task<IResult> GetSessions(
        ShopkeeperDbContext db,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = tenant.GetUserId(httpContext.User);
        if (!userId.HasValue)
        {
            return Results.Unauthorized();
        }

        var sessions = await db.RefreshTokens
            .AsNoTracking()
            .Where(x => x.UserAccountId == userId.Value)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new SessionView(
                x.Id,
                x.ShopMembership.ShopId,
                x.ShopMembership.Role.ToString(),
                x.DeviceId,
                x.DeviceName,
                x.CreatedAtUtc,
                x.ExpiresAtUtc,
                x.LastSeenAtUtc,
                x.RevokedAtUtc.HasValue))
            .ToListAsync(ct);

        return Results.Ok(sessions);
    }

    private static async Task<IResult> RevokeSession(
        Guid id,
        ShopkeeperDbContext db,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = tenant.GetUserId(httpContext.User);
        if (!userId.HasValue)
        {
            return Results.Unauthorized();
        }

        var session = await db.RefreshTokens
            .Include(x => x.ShopMembership)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserAccountId == userId.Value, ct);

        if (session is null)
        {
            return Results.NotFound();
        }

        if (!session.RevokedAtUtc.HasValue)
        {
            session.RevokedAtUtc = SystemClock.Instance.GetCurrentInstant();
            db.AuditLogs.Add(new Domain.AuditLog
            {
                TenantId = session.ShopMembership.ShopId,
                UserAccountId = userId.Value,
                Action = "account.session.revoke",
                EntityName = nameof(Domain.RefreshToken),
                EntityId = session.Id,
                PayloadJson = "{}"
            });
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new { sessionId = session.Id, status = "revoked" });
    }

    private static async Task<IResult> RevokeAllSessions(
        ShopkeeperDbContext db,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = tenant.GetUserId(httpContext.User);
        if (!userId.HasValue)
        {
            return Results.Unauthorized();
        }

        var sessions = await db.RefreshTokens
            .Include(x => x.ShopMembership)
            .Where(x => x.UserAccountId == userId.Value && !x.RevokedAtUtc.HasValue)
            .ToListAsync(ct);

        if (sessions.Count == 0)
        {
            return Results.Ok(new { revokedCount = 0, status = "no-active-sessions" });
        }

        var revokedAtUtc = SystemClock.Instance.GetCurrentInstant();
        var tenantId = tenant.GetTenantId(httpContext.User) ?? Guid.Empty;

        foreach (var session in sessions)
        {
            session.RevokedAtUtc = revokedAtUtc;
            db.AuditLogs.Add(new Domain.AuditLog
            {
                TenantId = tenantId,
                UserAccountId = userId.Value,
                Action = "account.session.revoke_all",
                EntityName = nameof(Domain.RefreshToken),
                EntityId = session.Id,
                PayloadJson = "{}"
            });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { revokedCount = sessions.Count, status = "revoked" });
    }

    private static async Task<IResult> GetLinkedIdentities(
        ShopkeeperDbContext db,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = tenant.GetUserId(httpContext.User);
        if (!userId.HasValue)
        {
            return Results.Unauthorized();
        }

        var identities = await db.AuthIdentities
            .AsNoTracking()
            .Where(x => x.UserAccountId == userId.Value)
            .OrderBy(x => x.Provider)
            .Select(x => new LinkedIdentityView(
                x.Provider,
                x.ProviderSubject,
                x.Email,
                x.EmailVerified,
                x.CreatedAtUtc,
                x.LastUsedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(identities);
    }
}
