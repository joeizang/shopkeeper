using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class SaleReadService(ShopkeeperDbContext db, ApiCacheService cache)
{
    private static readonly TimeSpan DetailTtl = TimeSpan.FromSeconds(30);

    public Task<CachedApiResult<SaleDetailResponse?>> GetSaleAsync(Guid tenantId, Guid saleId, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            $"tenant:{tenantId}:sale:{saleId}",
            DetailTtl,
            [ApiCacheTags.Sales(tenantId)],
            async token =>
            {
                var sale = await db.Sales
                    .AsNoTracking()
                    .Include(x => x.Lines)
                    .Include(x => x.Payments)
                    .Include(x => x.CreditAccount)
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == saleId, token);

                if (sale is null) return null;

                return new SaleDetailResponse(
                    sale.Id,
                    sale.SaleNumber,
                    sale.CustomerName,
                    sale.CustomerPhone,
                    sale.Subtotal,
                    sale.VatAmount,
                    sale.DiscountAmount,
                    sale.TotalAmount,
                    sale.OutstandingAmount,
                    sale.Status.ToString(),
                    sale.IsCredit,
                    sale.DueDateUtc,
                    sale.IsVoided,
                    sale.UpdatedAtUtc,
                    sale.Lines.Select(x => new SaleLineView(x.Id, x.InventoryItemId, x.ProductNameSnapshot, x.Quantity, x.UnitPrice, x.LineTotal)).ToList(),
                    sale.Payments.Select(x => new SalePaymentView(x.Id, x.Method.ToString(), x.Amount, x.Reference, x.CreatedAtUtc)).ToList());
            },
            ct);
    }
}
