using Microsoft.EntityFrameworkCore;
using NodaTime;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class ReportingReadService(ShopkeeperDbContext db, ReportingService reporting, ApiCacheService cache)
{
    private static readonly TimeSpan ExpensesTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InventoryReportTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SalesReportTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ProfitLossReportTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CreditorsReportTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ReportJobsTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReportFilesTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReportJobTtl = TimeSpan.FromSeconds(5);

    public Task<CachedApiResult<IReadOnlyList<ExpenseView>>> GetExpensesAsync(Guid tenantId, Instant? fromUtc, Instant? toUtc, CancellationToken ct)
    {
        return cache.GetOrSetAsync<IReadOnlyList<ExpenseView>>(
            ApiCacheKeys.ExpenseList(tenantId, fromUtc, toUtc),
            ExpensesTtl,
            [ApiCacheTags.Expenses(tenantId), ApiCacheTags.Reports(tenantId)],
            token => reporting.ListExpenses(tenantId, fromUtc, toUtc, token),
            ct);
    }

    public Task<CachedApiResult<InventoryReportResponse>> GetInventoryReportAsync(Guid tenantId, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.InventoryReport(tenantId),
            InventoryReportTtl,
            [ApiCacheTags.Inventory(tenantId), ApiCacheTags.Reports(tenantId)],
            token => reporting.BuildInventoryReport(tenantId, token),
            ct);
    }

    public Task<CachedApiResult<SalesReportResponse>> GetSalesReportAsync(Guid tenantId, Instant fromUtc, Instant toUtc, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.SalesReport(tenantId, fromUtc, toUtc),
            SalesReportTtl,
            [ApiCacheTags.Sales(tenantId), ApiCacheTags.Credits(tenantId), ApiCacheTags.Reports(tenantId), ApiCacheTags.Inventory(tenantId)],
            token => reporting.BuildSalesReport(tenantId, fromUtc, toUtc, token),
            ct);
    }

    public Task<CachedApiResult<ProfitLossReportResponse>> GetProfitLossReportAsync(Guid tenantId, Instant fromUtc, Instant toUtc, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.ProfitLossReport(tenantId, fromUtc, toUtc),
            ProfitLossReportTtl,
            [ApiCacheTags.Sales(tenantId), ApiCacheTags.Expenses(tenantId), ApiCacheTags.Reports(tenantId), ApiCacheTags.Inventory(tenantId)],
            token => reporting.BuildProfitLossReport(tenantId, fromUtc, toUtc, token),
            ct);
    }

    public Task<CachedApiResult<CreditorsReportResponse>> GetCreditorsReportAsync(Guid tenantId, Instant? fromUtc, Instant? toUtc, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.CreditorsReport(tenantId, fromUtc, toUtc),
            CreditorsReportTtl,
            [ApiCacheTags.Credits(tenantId), ApiCacheTags.Reports(tenantId), ApiCacheTags.Sales(tenantId)],
            token => reporting.BuildCreditorsReport(tenantId, fromUtc, toUtc, token),
            ct);
    }

    public Task<CachedApiResult<IReadOnlyList<ReportJobView>>> GetReportJobsAsync(Guid tenantId, CancellationToken ct)
    {
        return cache.GetOrSetAsync<IReadOnlyList<ReportJobView>>(
            ApiCacheKeys.ReportJobs(tenantId),
            ReportJobsTtl,
            [ApiCacheTags.Reports(tenantId)],
            async token => await db.ReportJobs
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.RequestedAtUtc)
                .Select(x => new ReportJobView(x.Id, x.ReportType, x.Format, x.Status, x.FilterJson, x.ReportFileId, x.RequestedAtUtc, x.CompletedAtUtc, x.FailureReason))
                .ToListAsync(token),
            ct);
    }

    public Task<CachedApiResult<ReportJobView?>> GetReportJobAsync(Guid tenantId, Guid reportJobId, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.ReportJob(tenantId, reportJobId),
            ReportJobTtl,
            [ApiCacheTags.Reports(tenantId)],
            async token => await db.ReportJobs
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == reportJobId)
                .Select(x => new ReportJobView(x.Id, x.ReportType, x.Format, x.Status, x.FilterJson, x.ReportFileId, x.RequestedAtUtc, x.CompletedAtUtc, x.FailureReason))
                .FirstOrDefaultAsync(token),
            ct);
    }

    public Task<CachedApiResult<IReadOnlyList<ReportFileView>>> GetReportFilesAsync(Guid tenantId, CancellationToken ct)
    {
        return cache.GetOrSetAsync<IReadOnlyList<ReportFileView>>(
            ApiCacheKeys.ReportFiles(tenantId),
            ReportFilesTtl,
            [ApiCacheTags.Reports(tenantId)],
            async token => await db.ReportFiles
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new ReportFileView(
                    x.Id,
                    x.ReportType,
                    x.Format,
                    x.FileName,
                    x.ContentType,
                    x.ByteLength,
                    x.CreatedAtUtc))
                .ToListAsync(token),
            ct);
    }
}
