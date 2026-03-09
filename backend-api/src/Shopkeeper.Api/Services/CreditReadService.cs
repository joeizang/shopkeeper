using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class CreditReadService(ShopkeeperDbContext db, ApiCacheService cache)
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromSeconds(20);

    public Task<CachedApiResult<PagedResponse<CreditAccountView>>> ListCreditsAsync(Guid tenantId, int page, int limit, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.CreditList(tenantId, page, limit),
            ListTtl,
            [ApiCacheTags.Credits(tenantId), ApiCacheTags.Reports(tenantId)],
            async token =>
            {
                var query = db.CreditAccounts
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenantId);

                var total = await query.CountAsync(token);
                var credits = await query
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .Skip((page - 1) * limit)
                    .Take(limit)
                    .Select(x => new CreditAccountView(x.Id, x.SaleId, x.DueDateUtc, x.OutstandingAmount, x.Status))
                    .ToListAsync(token);

                return new PagedResponse<CreditAccountView>(total, page, limit, credits);
            },
            ct);
    }

    public Task<CachedApiResult<CreditDetailResponse?>> GetCreditAsync(Guid tenantId, Guid saleId, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.CreditDetail(tenantId, saleId),
            DetailTtl,
            [ApiCacheTags.Credits(tenantId), ApiCacheTags.Reports(tenantId), ApiCacheTags.Sales(tenantId)],
            async token =>
            {
                var credit = await db.CreditAccounts
                    .AsNoTracking()
                    .Include(x => x.Repayments)
                        .ThenInclude(x => x.SalePayment)
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.SaleId == saleId, token);

                return credit is null
                    ? null
                    : new CreditDetailResponse(
                        new CreditAccountView(credit.Id, credit.SaleId, credit.DueDateUtc, credit.OutstandingAmount, credit.Status),
                        credit.Repayments
                            .OrderByDescending(r => r.CreatedAtUtc)
                            .Select(r => new CreditRepaymentView(
                                r.Id,
                                r.Amount,
                                r.SalePayment.Method,
                                r.SalePayment.Reference,
                                r.Notes,
                                r.CreatedAtUtc))
                            .ToList());
            },
            ct);
    }
}
