using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class ShopEndpoints
{
    public static IEndpointRouteBuilder MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shops").RequireAuthorization();

        group.MapGet("/me", GetMyShops);
        group.MapPost("/", CreateShop);
        group.MapPatch("/{shopId:guid}/settings", UpdateShopSettings)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });
        group.MapGet("/{shopId:guid}/staff", ListStaff)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });
        group.MapPost("/{shopId:guid}/staff/invite", InviteStaff)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });
        group.MapPost("/{shopId:guid}/staff/{staffId:guid}/activate", ActivateStaff)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });
        group.MapPatch("/{shopId:guid}/staff/{staffId:guid}", UpdateStaffMembership)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });

        return app;
    }

    private static async Task<IResult> GetMyShops(
        ShopReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = tenant.GetUserId(httpContext.User);
        if (!userId.HasValue)
        {
            return Results.Unauthorized();
        }

        var cached = await reads.GetMyShopsAsync(userId.Value, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> CreateShop(
        [FromBody] CreateShopRequest request,
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

        var shopCodeSeed = request.Name.Replace(" ", string.Empty).ToUpperInvariant();
        var prefix = shopCodeSeed[..Math.Min(shopCodeSeed.Length, 6)];

        string shopCode;
        int attempts = 0;
        do
        {
            shopCode = $"{prefix}{Random.Shared.Next(1000, 9999)}";
            attempts++;
        }
        while (attempts < 5 && await db.Shops.AnyAsync(x => x.Code == shopCode, ct));

        var shop = new Shop
        {
            Name = request.Name,
            Code = shopCode,
            VatEnabled = request.VatEnabled,
            VatRate = request.VatRate <= 0 ? 0.075m : request.VatRate,
            DefaultDiscountPercent = 0m
        };

        var membership = new ShopMembership
        {
            Shop = shop,
            UserAccountId = userId.Value,
            Role = MembershipRole.Owner,
            IsActive = true
        };

        db.Shops.Add(shop);
        db.ShopMemberships.Add(membership);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.UserShops(userId.Value)], ct);

        return Results.Created($"/api/v1/shops/{shop.Id}", new ShopView(
            shop.Id,
            shop.Name,
            shop.Code,
            shop.VatEnabled,
            shop.VatRate,
            shop.DefaultDiscountPercent,
            NormalizeRoleName(membership.Role),
            Convert.ToBase64String(shop.RowVersion)));
    }

    private static async Task<IResult> UpdateShopSettings(
        Guid shopId,
        [FromBody] UpdateShopVatSettingsRequest request,
        ShopkeeperDbContext db,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue || tenantId.Value != shopId)
        {
            return Results.Forbid();
        }

        if (request.VatRate < 0 || request.VatRate > 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["vatRate"] = ["VAT rate must be between 0 and 1."]
            });
        }

        if (request.DefaultDiscountPercent < 0 || request.DefaultDiscountPercent > 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["defaultDiscountPercent"] = ["Default discount percent must be between 0 and 1."]
            });
        }

        var membership = await db.ShopMemberships
            .FirstOrDefaultAsync(x =>
                x.ShopId == shopId &&
                x.UserAccountId == tenant.GetUserId(httpContext.User) &&
                x.Role == MembershipRole.Owner &&
                x.IsActive, ct);

        if (membership is null)
        {
            return Results.Forbid();
        }

        var shop = await db.Shops.FirstOrDefaultAsync(x => x.Id == shopId, ct);
        if (shop is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.RowVersionBase64))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "Precondition required", detail: "rowVersionBase64 is required for shop settings updates.");
        }

        var requestVersion = Convert.FromBase64String(request.RowVersionBase64);
        if (!shop.RowVersion.SequenceEqual(requestVersion))
        {
            return Results.Conflict(new { message = "Shop settings changed. Refresh and try again." });
        }

        shop.VatEnabled = request.VatEnabled;
        shop.VatRate = request.VatEnabled ? request.VatRate : 0m;
        shop.DefaultDiscountPercent = request.DefaultDiscountPercent;
        await db.SaveChangesAsync(ct);

        var userIds = await GetShopMemberUserIdsAsync(db, shopId, ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Staff(shopId), .. userIds.Select(ApiCacheTags.UserShops)], ct);

        return Results.Ok(new ShopView(
            shop.Id,
            shop.Name,
            shop.Code,
            shop.VatEnabled,
            shop.VatRate,
            shop.DefaultDiscountPercent,
            NormalizeRoleName(membership.Role),
            Convert.ToBase64String(shop.RowVersion)));
    }

    private static async Task<IResult> InviteStaff(
        Guid shopId,
        [FromBody] InviteStaffRequest request,
        ShopkeeperDbContext db,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        UserManager<UserAccount> userManager,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue || tenantId.Value != shopId)
        {
            return Results.Forbid();
        }

        var user = await db.Users.FirstOrDefaultAsync(x =>
            (!string.IsNullOrWhiteSpace(request.Email) && x.Email == request.Email)
            || (!string.IsNullOrWhiteSpace(request.Phone) && x.PhoneNumber == request.Phone), ct);

        if (string.IsNullOrWhiteSpace(request.TemporaryPassword))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["temporaryPassword"] = ["A temporary password is required to invite staff."]
            });
        }

        if (user is null)
        {
            user = new UserAccount
            {
                FullName = request.FullName,
                Email = request.Email,
                UserName = request.Email?.Trim().ToLowerInvariant() ?? $"phone_{request.Phone}",
                PhoneNumber = request.Phone,
                EmailConfirmed = !string.IsNullOrWhiteSpace(request.Email)
            };

            var createResult = await userManager.CreateAsync(user, request.TemporaryPassword);
            if (!createResult.Succeeded)
            {
                return Results.ValidationProblem(createResult.Errors
                    .GroupBy(x => x.Code)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Description).ToArray()));
            }
        }

        var existing = await db.ShopMemberships
            .AnyAsync(x => x.ShopId == shopId && x.UserAccountId == user.Id, ct);

        if (existing)
        {
            return Results.Conflict(new { message = "Staff membership already exists." });
        }

        var requestedRole = RoleCapabilities.ParseRole(request.Role);
        if (requestedRole == MembershipRole.Owner)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] = ["Use ShopManager or Salesperson for staff accounts."]
            });
        }

        var membership = new ShopMembership
        {
            ShopId = shopId,
            UserAccount = user,
            Role = requestedRole,
            IsActive = false
        };

        db.ShopMemberships.Add(membership);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Staff(shopId), ApiCacheTags.UserShops(user.Id)], ct);

        return Results.Ok(ToStaffView(membership, user));
    }

    private static async Task<IResult> ListStaff(
        Guid shopId,
        ShopkeeperDbContext db,
        ShopReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue || tenantId.Value != shopId)
        {
            return Results.Forbid();
        }

        var isOwner = await db.ShopMemberships.AnyAsync(x =>
            x.ShopId == shopId &&
            x.UserAccountId == tenant.GetUserId(httpContext.User) &&
            x.Role == MembershipRole.Owner &&
            x.IsActive, ct);

        if (!isOwner)
        {
            return Results.Forbid();
        }

        var cached = await reads.ListStaffAsync(shopId, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> ActivateStaff(
        Guid shopId,
        Guid staffId,
        ShopkeeperDbContext db,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue || tenantId.Value != shopId)
        {
            return Results.Forbid();
        }

        var membership = await db.ShopMemberships
            .FirstOrDefaultAsync(x => x.ShopId == shopId && x.Id == staffId, ct);

        if (membership is null)
        {
            return Results.NotFound();
        }

        membership.IsActive = true;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Staff(shopId), ApiCacheTags.UserShops(membership.UserAccountId)], ct);

        return Results.Ok(new { staffId = membership.Id, status = "active" });
    }

    private static async Task<IResult> UpdateStaffMembership(
        Guid shopId,
        Guid staffId,
        [FromBody] UpdateStaffMembershipRequest request,
        ShopkeeperDbContext db,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue || tenantId.Value != shopId)
        {
            return Results.Forbid();
        }

        var actingUserId = tenant.GetUserId(httpContext.User);
        var isOwner = actingUserId.HasValue && await db.ShopMemberships.AnyAsync(x =>
            x.ShopId == shopId &&
            x.UserAccountId == actingUserId.Value &&
            x.Role == MembershipRole.Owner &&
            x.IsActive, ct);

        if (!isOwner)
        {
            return Results.Forbid();
        }

        var membership = await db.ShopMemberships
            .Include(x => x.UserAccount)
            .FirstOrDefaultAsync(x => x.ShopId == shopId && x.Id == staffId, ct);

        if (membership is null)
        {
            return Results.NotFound();
        }

        var requestedRole = RoleCapabilities.ParseRole(request.Role);
        if (membership.Role == MembershipRole.Owner || requestedRole == MembershipRole.Owner)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] = ["Owner role cannot be assigned or changed with this endpoint."]
            });
        }

        membership.Role = requestedRole;
        membership.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Staff(shopId), ApiCacheTags.UserShops(membership.UserAccountId)], ct);

        return Results.Ok(ToStaffView(membership, membership.UserAccount));
    }

    private static async Task<IReadOnlyList<Guid>> GetShopMemberUserIdsAsync(ShopkeeperDbContext db, Guid shopId, CancellationToken ct)
    {
        return await db.ShopMemberships
            .AsNoTracking()
            .Where(x => x.ShopId == shopId)
            .Select(x => x.UserAccountId)
            .Distinct()
            .ToListAsync(ct);
    }

    private static StaffMembershipView ToStaffView(ShopMembership membership, UserAccount user)
    {
        return new StaffMembershipView(
            membership.Id,
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            NormalizeRoleName(membership.Role),
            membership.IsActive,
            membership.CreatedAtUtc);
    }

    private static string NormalizeRoleName(MembershipRole role) => role.ToString();
}
