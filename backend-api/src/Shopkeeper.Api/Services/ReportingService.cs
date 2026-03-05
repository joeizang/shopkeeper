using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Services;

public sealed class ReportingService(ShopkeeperDbContext db)
{
    public async Task<InventoryReportResponse> BuildInventoryReport(Guid tenantId, CancellationToken ct)
    {
        var items = await db.InventoryItems
            .Where(x => x.TenantId == tenantId && !x.IsDeleted)
            .OrderBy(x => x.ProductName)
            .ToListAsync(ct);

        var rows = items.Select(x =>
        {
            var costValue = x.CostPrice * x.Quantity;
            var sellingValue = x.SellingPrice * x.Quantity;
            return new InventoryReportRow(
                x.Id,
                x.ProductName,
                x.Quantity,
                x.CostPrice,
                x.SellingPrice,
                costValue,
                sellingValue,
                x.ExpiryDate);
        }).ToList();

        return new InventoryReportResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            TotalProducts: rows.Count,
            TotalUnits: rows.Sum(x => x.Quantity),
            LowStockItems: rows.Count(x => x.Quantity <= 2),
            TotalCostValue: rows.Sum(x => x.CostValue),
            TotalSellingValue: rows.Sum(x => x.SellingValue),
            Items: rows);
    }

    public async Task<SalesReportResponse> BuildSalesReport(Guid tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var sales = await db.Sales
            .Include(x => x.Payments)
            .Where(x => x.TenantId == tenantId && !x.IsVoided && x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc <= toUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var daily = sales
            .GroupBy(x => DateOnly.FromDateTime(x.CreatedAtUtc))
            .OrderBy(x => x.Key)
            .Select(g => new SalesDailySummaryRow(
                g.Key,
                g.Count(),
                g.Sum(s => s.TotalAmount),
                g.Sum(s => s.OutstandingAmount)))
            .ToList();

        var payments = sales
            .SelectMany(x => x.Payments)
            .GroupBy(x => x.Method.ToString())
            .OrderBy(x => x.Key)
            .Select(g => new SalesPaymentSummaryRow(g.Key, g.Sum(p => p.Amount)))
            .ToList();

        return new SalesReportResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            SalesCount: sales.Count,
            Revenue: sales.Sum(x => x.TotalAmount),
            VatAmount: sales.Sum(x => x.VatAmount),
            DiscountAmount: sales.Sum(x => x.DiscountAmount),
            OutstandingAmount: sales.Sum(x => x.OutstandingAmount),
            Daily: daily,
            Payments: payments);
    }

    public async Task<ProfitLossReportResponse> BuildProfitLossReport(Guid tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var sales = await db.Sales
            .Include(x => x.Lines)
            .Where(x => x.TenantId == tenantId && !x.IsVoided && x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc <= toUtc)
            .ToListAsync(ct);

        var lineItemIds = sales
            .SelectMany(x => x.Lines)
            .Select(x => x.InventoryItemId)
            .Distinct()
            .ToList();

        var itemCostMap = await db.InventoryItems
            .Where(x => x.TenantId == tenantId && lineItemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.CostPrice, ct);

        var revenue = sales.Sum(x => x.TotalAmount);
        var cogs = sales
            .SelectMany(x => x.Lines)
            .Sum(line =>
            {
                var cost = itemCostMap.GetValueOrDefault(line.InventoryItemId, 0m);
                return cost * line.Quantity;
            });
        const decimal expenses = 0m;
        var gross = revenue - cogs;
        var net = gross - expenses;

        return new ProfitLossReportResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Revenue: revenue,
            Cogs: cogs,
            GrossProfit: gross,
            Expenses: expenses,
            NetProfitLoss: net);
    }

    public async Task<CreditorsReportResponse> BuildCreditorsReport(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var query = db.CreditAccounts
            .Include(x => x.Sale)
            .ThenInclude(x => x.Lines)
            .Where(x => x.TenantId == tenantId && x.OutstandingAmount > 0m && x.Status != CreditStatus.Settled);

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.DueDateUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.DueDateUtc <= toUtc.Value);
        }

        var accounts = await query
            .OrderBy(x => x.DueDateUtc)
            .ToListAsync(ct);

        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = accounts.Select(x =>
        {
            var dueDate = DateOnly.FromDateTime(x.DueDateUtc);
            var daysOverdue = Math.Max(0, nowDate.DayNumber - dueDate.DayNumber);
            var customerName = string.IsNullOrWhiteSpace(x.Sale.CustomerName) ? "Walk-in Customer" : x.Sale.CustomerName!;
            var summary = string.Join(", ", x.Sale.Lines.Select(l => $"{l.ProductNameSnapshot} x{l.Quantity}"));

            return new CreditorReportRow(
                x.Id,
                x.SaleId,
                x.Sale.SaleNumber,
                customerName,
                string.IsNullOrWhiteSpace(summary) ? "Credit Sale" : summary,
                x.DueDateUtc,
                daysOverdue,
                x.OutstandingAmount,
                x.Status.ToString());
        }).ToList();

        return new CreditorsReportResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            OpenCredits: rows.Count,
            TotalOutstanding: rows.Sum(x => x.OutstandingAmount),
            Credits: rows);
    }
}
