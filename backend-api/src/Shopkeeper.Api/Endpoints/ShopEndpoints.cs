using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Endpoints;

public static class ShopEndpoints
{
    public static IEndpointRouteBuilder MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shops").RequireAuthorization();

        group.MapGet("/me", GetMyShops);
        group.MapPost("/", CreateShop);
        group.MapPost("/{shopId:guid}/staff/invite", InviteStaff)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });
        group.MapPost("/{shopId:guid}/staff/{staffId:guid}/activate", ActivateStaff)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });

        return app;
    }

    private static async Task<IResult> GetMyShops(
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

        var shops = await db.ShopMemberships
            .Where(x => x.UserAccountId == userId.Value && x.IsActive)
            .Join(db.Shops, m => m.ShopId, s => s.Id, (m, s) => new ShopView(
                s.Id,
                s.Name,
                s.Code,
                s.VatEnabled,
                s.VatRate,
                m.Role))
            .ToListAsync(ct);

        return Results.Ok(shops);
    }

    private static async Task<IResult> CreateShop(
        [FromBody] CreateShopRequest request,
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

        var shopCodeSeed = request.Name.Replace(" ", string.Empty).ToUpperInvariant();
        var shop = new Shop
        {
            Name = request.Name,
            Code = $"{shopCodeSeed[..Math.Min(shopCodeSeed.Length, 6)]}{Random.Shared.Next(1000, 9999)}",
            VatEnabled = request.VatEnabled,
            VatRate = request.VatRate <= 0 ? 0.075m : request.VatRate
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

        return Results.Created($"/api/v1/shops/{shop.Id}", new ShopView(
            shop.Id,
            shop.Name,
            shop.Code,
            shop.VatEnabled,
            shop.VatRate,
            membership.Role));
    }

    private static async Task<IResult> InviteStaff(
        Guid shopId,
        [FromBody] InviteStaffRequest request,
        ShopkeeperDbContext db,
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

            var createResult = string.IsNullOrWhiteSpace(request.TemporaryPassword)
                ? await userManager.CreateAsync(user)
                : await userManager.CreateAsync(user, request.TemporaryPassword);

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

        var membership = new ShopMembership
        {
            ShopId = shopId,
            UserAccount = user,
            Role = MembershipRole.Staff,
            IsActive = false
        };

        db.ShopMemberships.Add(membership);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { staffId = membership.Id, status = "invited" });
    }

    private static async Task<IResult> ActivateStaff(
        Guid shopId,
        Guid staffId,
        ShopkeeperDbContext db,
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

        return Results.Ok(new { staffId = membership.Id, status = "active" });
    }
}
