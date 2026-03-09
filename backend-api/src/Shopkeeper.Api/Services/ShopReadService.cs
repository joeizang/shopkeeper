using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class ShopReadService(ShopkeeperDbContext db, ApiCacheService cache)
{
    private static readonly TimeSpan ShopsTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StaffTtl = TimeSpan.FromSeconds(30);

    public Task<CachedApiResult<IReadOnlyList<ShopView>>> GetMyShopsAsync(Guid userId, CancellationToken ct)
    {
        return cache.GetOrSetAsync<IReadOnlyList<ShopView>>(
            ApiCacheKeys.UserShops(userId),
            ShopsTtl,
            [ApiCacheTags.UserShops(userId)],
            async token =>
            {
                var shops = await db.ShopMemberships
                    .AsNoTracking()
                    .Where(x => x.UserAccountId == userId && x.IsActive)
                    .Join(db.Shops.AsNoTracking(), m => m.ShopId, s => s.Id, (m, s) => new ShopView(
                        s.Id,
                        s.Name,
                        s.Code,
                        s.VatEnabled,
                        s.VatRate,
                        s.DefaultDiscountPercent,
                        NormalizeRoleName(m.Role),
                        Convert.ToBase64String(s.RowVersion)))
                    .ToListAsync(token);

                return shops;
            },
            ct);
    }

    public Task<CachedApiResult<IReadOnlyList<StaffMembershipView>>> ListStaffAsync(Guid shopId, CancellationToken ct)
    {
        return cache.GetOrSetAsync<IReadOnlyList<StaffMembershipView>>(
            ApiCacheKeys.StaffList(shopId),
            StaffTtl,
            [ApiCacheTags.Staff(shopId)],
            async token =>
            {
                var staff = await db.ShopMemberships
                    .AsNoTracking()
                    .Where(x => x.ShopId == shopId && x.Role != MembershipRole.Owner)
                    .Include(x => x.UserAccount)
                    .OrderBy(x => x.Role)
                    .ThenBy(x => x.CreatedAtUtc)
                    .Select(x => new StaffMembershipView(
                        x.Id,
                        x.UserAccount.Id,
                        x.UserAccount.FullName,
                        x.UserAccount.Email,
                        x.UserAccount.PhoneNumber,
                        NormalizeRoleName(x.Role),
                        x.IsActive,
                        x.CreatedAtUtc))
                    .ToListAsync(token);

                return staff;
            },
            ct);
    }

    private static string NormalizeRoleName(MembershipRole role) => role.ToString();
}
