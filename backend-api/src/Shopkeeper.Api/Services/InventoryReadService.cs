using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class InventoryReadService(ShopkeeperDbContext db, ApiCacheService cache)
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromSeconds(30);

    public Task<CachedApiResult<PagedResponse<InventoryItemView>>> ListItemsAsync(Guid tenantId, int page, int limit, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.InventoryList(tenantId, page, limit),
            ListTtl,
            [ApiCacheTags.Inventory(tenantId), ApiCacheTags.Reports(tenantId)],
            async token =>
            {
                var query = db.InventoryItems
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenantId && !x.IsDeleted);

                var total = await query.CountAsync(token);
                var items = await query
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .Skip((page - 1) * limit)
                    .Take(limit)
                    .Select(x => new InventoryItemView(
                        x.Id,
                        x.ProductName,
                        x.ModelNumber,
                        x.SerialNumber,
                        x.Quantity,
                        x.ExpiryDate,
                        x.CostPrice,
                        x.SellingPrice,
                        x.ItemType,
                        x.ConditionGrade,
                        x.ConditionNotes,
                        x.Photos.Select(p => p.PhotoUri).ToList(),
                        x.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(x.RowVersion)))
                    .ToListAsync(token);

                return new PagedResponse<InventoryItemView>(total, page, limit, items);
            },
            ct);
    }

    public Task<CachedApiResult<InventoryItemView?>> GetItemAsync(Guid tenantId, Guid itemId, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.InventoryItem(tenantId, itemId),
            DetailTtl,
            [ApiCacheTags.Inventory(tenantId), ApiCacheTags.Reports(tenantId)],
            async token =>
            {
                var item = await db.InventoryItems
                    .AsNoTracking()
                    .Include(x => x.Photos)
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == itemId && !x.IsDeleted, token);

                return item is null ? null : ToView(item);
            },
            ct);
    }

    private static InventoryItemView ToView(InventoryItem item)
        => new(
            item.Id,
            item.ProductName,
            item.ModelNumber,
            item.SerialNumber,
            item.Quantity,
            item.ExpiryDate,
            item.CostPrice,
            item.SellingPrice,
            item.ItemType,
            item.ConditionGrade,
            item.ConditionNotes,
            item.Photos.Select(x => x.PhotoUri).ToList(),
            item.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(item.RowVersion));
}
