using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public enum UpdateExpenseStatus { Ok, NotFound, Conflict }
public sealed record UpdateExpenseResult(UpdateExpenseStatus Status, ExpenseView? View);

public sealed class ReportingService(ShopkeeperDbContext db, ReportDocumentRenderer renderer, ReportJobChannel jobChannel, ApiCacheService cache)
{
    public async Task<IReadOnlyList<ExpenseView>> ListExpenses(Guid tenantId, Instant? fromUtc, Instant? toUtc, CancellationToken ct)
    {
        var query = db.Expenses
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.ExpenseDateUtc)
            .AsQueryable();

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.ExpenseDateUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.ExpenseDateUtc <= toUtc.Value);
        }

        var items = await query.ToListAsync(ct);
        return items.Select(ToView).ToList();
    }

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
            GeneratedAtUtc: SystemClock.Instance.GetCurrentInstant(),
            TotalProducts: rows.Count,
            TotalUnits: rows.Sum(x => x.Quantity),
            LowStockItems: rows.Count(x => x.Quantity <= 2),
            TotalCostValue: rows.Sum(x => x.CostValue),
            TotalSellingValue: rows.Sum(x => x.SellingValue),
            Items: rows);
    }

    public async Task<SalesReportResponse> BuildSalesReport(Guid tenantId, Instant fromUtc, Instant toUtc, CancellationToken ct)
    {
        var sales = await db.Sales
            .Include(x => x.Payments)
            .Where(x => x.TenantId == tenantId && !x.IsVoided && x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc <= toUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var daily = sales
            .GroupBy(x => x.CreatedAtUtc.InUtc().Date)
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
            GeneratedAtUtc: SystemClock.Instance.GetCurrentInstant(),
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

    public async Task<ProfitLossReportResponse> BuildProfitLossReport(Guid tenantId, Instant fromUtc, Instant toUtc, CancellationToken ct)
    {
        var revenue = await db.Sales
            .Where(x => x.TenantId == tenantId && !x.IsVoided && x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc <= toUtc)
            .SumAsync(x => (decimal?)x.TotalAmount, ct) ?? 0m;

        var cogs = await (
            from sl in db.SaleLines
            join s in db.Sales on sl.SaleId equals s.Id
            join item in db.InventoryItems on sl.InventoryItemId equals item.Id
            where s.TenantId == tenantId && !s.IsVoided
                  && s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc <= toUtc
                  && item.TenantId == tenantId
            select (decimal?)(sl.Quantity * sl.CostPriceSnapshot)
        ).SumAsync(ct) ?? 0m;
        var expensesByCategory = await db.Expenses
            .Where(x => x.TenantId == tenantId && x.ExpenseDateUtc >= fromUtc && x.ExpenseDateUtc <= toUtc)
            .GroupBy(x => x.Category)
            .OrderBy(x => x.Key)
            .Select(g => new ExpenseCategorySummaryRow(g.Key, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        var expenses = expensesByCategory.Sum(x => x.Amount);
        var gross = revenue - cogs;
        var net = gross - expenses;

        return new ProfitLossReportResponse(
            GeneratedAtUtc: SystemClock.Instance.GetCurrentInstant(),
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Revenue: revenue,
            Cogs: cogs,
            GrossProfit: gross,
            Expenses: expenses,
            NetProfitLoss: net,
            ExpenseBreakdown: expensesByCategory);
    }

    public async Task<CreditorsReportResponse> BuildCreditorsReport(Guid tenantId, Instant? fromUtc, Instant? toUtc, CancellationToken ct)
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

        var nowDate = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
        var rows = accounts.Select(x =>
        {
            var dueDate = x.DueDateUtc.InUtc().Date;
            var daysOverdue = Math.Max(0, Period.Between(dueDate, nowDate, PeriodUnits.Days).Days);
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
            GeneratedAtUtc: SystemClock.Instance.GetCurrentInstant(),
            FromUtc: fromUtc,
            ToUtc: toUtc,
            OpenCredits: rows.Count,
            TotalOutstanding: rows.Sum(x => x.OutstandingAmount),
            Credits: rows);
    }

    public async Task<ExpenseView> CreateExpense(Guid tenantId, Guid? userId, CreateExpenseRequest request, CancellationToken ct)
    {
        var expense = new Expense
        {
            TenantId = tenantId,
            Title = request.Title.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim(),
            Amount = request.Amount,
            ExpenseDateUtc = request.ExpenseDateUtc,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedByUserAccountId = userId
        };

        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Expenses(tenantId), ApiCacheTags.Reports(tenantId)], ct);
        return ToView(expense);
    }

    public async Task<UpdateExpenseResult> UpdateExpense(Guid tenantId, Guid expenseId, UpdateExpenseRequest request, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == expenseId, ct);
        if (expense is null)
        {
            return new UpdateExpenseResult(UpdateExpenseStatus.NotFound, null);
        }

        if (!string.IsNullOrWhiteSpace(request.RowVersionBase64))
        {
            var requestVersion = Convert.FromBase64String(request.RowVersionBase64);
            if (!expense.RowVersion.SequenceEqual(requestVersion))
            {
                return new UpdateExpenseResult(UpdateExpenseStatus.Conflict, null);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            expense.Title = request.Title.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            expense.Category = request.Category.Trim();
        }
        if (request.Amount.HasValue)
        {
            expense.Amount = request.Amount.Value;
        }
        if (request.ExpenseDateUtc.HasValue)
        {
            expense.ExpenseDateUtc = request.ExpenseDateUtc.Value;
        }
        if (request.Notes is not null)
        {
            expense.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        }

        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Expenses(tenantId), ApiCacheTags.Reports(tenantId)], ct);
        return new UpdateExpenseResult(UpdateExpenseStatus.Ok, ToView(expense));
    }

    public async Task<bool> DeleteExpense(Guid tenantId, Guid expenseId, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == expenseId, ct);
        if (expense is null)
        {
            return false;
        }

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Expenses(tenantId), ApiCacheTags.Reports(tenantId)], ct);
        return true;
    }

    public async Task<ReportJobView> QueueReportJob(
        Guid tenantId,
        Guid requestedByUserId,
        string reportType,
        string format,
        Instant? fromUtc,
        Instant? toUtc,
        CancellationToken ct)
    {
        var job = new ReportJob
        {
            TenantId = tenantId,
            RequestedByUserAccountId = requestedByUserId,
            ReportType = reportType,
            Format = format,
            Status = ReportJobStatuses.Pending,
            FilterJson = JsonSerializer.Serialize(new ReportFilterEnvelope(fromUtc, toUtc))
        };

        db.ReportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Reports(tenantId)], ct);
        jobChannel.Writer.TryWrite(job.Id);
        return ToView(job);
    }

    public async Task<ReportJob?> GetReportJob(Guid tenantId, Guid reportJobId, CancellationToken ct)
    {
        return await db.ReportJobs.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == reportJobId, ct);
    }

    public async Task<ReportJobView?> RetryReportJob(Guid tenantId, Guid reportJobId, CancellationToken ct)
    {
        var job = await db.ReportJobs.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == reportJobId, ct);
        if (job is null)
        {
            return null;
        }

        if (job.Status != ReportJobStatuses.Failed)
        {
            return null;
        }

        job.Status = ReportJobStatuses.Pending;
        job.CompletedAtUtc = null;
        job.ReportFileId = null;
        job.FailureReason = null;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Reports(tenantId)], ct);
        jobChannel.Writer.TryWrite(job.Id);
        return ToView(job);
    }

    public async Task<ReportJob?> ClaimNextPendingJob(CancellationToken ct)
    {
        var job = await db.ReportJobs
            .Where(x => x.Status == ReportJobStatuses.Pending)
            .OrderBy(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (job is null)
        {
            return null;
        }

        job.Status = ReportJobStatuses.InProgress;
        job.FailureReason = null;
        await db.SaveChangesAsync(ct);
        return job;
    }

    public async Task ExecuteQueuedReportJob(Guid reportJobId, CancellationToken ct)
    {
        var job = await db.ReportJobs.FirstOrDefaultAsync(x => x.Id == reportJobId, ct);
        if (job is null)
        {
            return;
        }

        try
        {
            var filters = ParseFilter(job.FilterJson);
            var document = await GenerateReportDocument(job.TenantId, job.ReportType, job.Format, filters.FromUtc, filters.ToUtc, ct);
            await CompleteReportJob(job, document.Content, document.FileName, document.ContentType, ct);
        }
        catch (Exception ex)
        {
            await FailReportJob(job, ex.Message, ct);
        }
    }

    public async Task<(byte[] Content, string FileName, string ContentType)> GenerateReportDocument(
        Guid tenantId,
        string reportType,
        string format,
        Instant? fromUtc,
        Instant? toUtc,
        CancellationToken ct)
    {
        var exportKind = format.Equals("pdf", StringComparison.OrdinalIgnoreCase)
            ? ExportKind.Pdf
            : ExportKind.Spreadsheet;
        var stamp = SystemClock.Instance.GetCurrentInstant().ToDateTimeUtc().ToString("yyyyMMddHHmmss");
        var normalizedType = reportType.Trim().ToLowerInvariant();

        return normalizedType switch
        {
            "inventory" => await BuildInventoryDocument(tenantId, exportKind, stamp, ct),
            "sales" => await BuildSalesDocument(tenantId, exportKind, stamp, fromUtc, toUtc, ct),
            "profit-loss" => await BuildProfitLossDocument(tenantId, exportKind, stamp, fromUtc, toUtc, ct),
            "creditors" => await BuildCreditorsDocument(tenantId, exportKind, stamp, fromUtc, toUtc, ct),
            _ => throw new InvalidOperationException($"Unsupported report type '{reportType}'.")
        };
    }

    public async Task<ReportFileView> SaveReportArtifact(
        Guid tenantId,
        Guid requestedByUserId,
        string reportType,
        string format,
        string fileName,
        string contentType,
        byte[] content,
        string? filterJson,
        CancellationToken ct)
    {
        var reportFile = new ReportFile
        {
            TenantId = tenantId,
            ReportType = reportType,
            Format = format,
            FileName = fileName,
            ContentType = contentType,
            ByteLength = content.LongLength,
            Content = content,
            CreatedByUserAccountId = requestedByUserId
        };

        var reportJob = new ReportJob
        {
            TenantId = tenantId,
            ReportType = reportType,
            Format = format,
            Status = "Completed",
            FilterJson = filterJson,
            RequestedByUserAccountId = requestedByUserId,
            ReportFile = reportFile,
            CompletedAtUtc = SystemClock.Instance.GetCurrentInstant()
        };

        db.ReportFiles.Add(reportFile);
        db.ReportJobs.Add(reportJob);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Reports(tenantId)], ct);

        return new ReportFileView(
            reportFile.Id,
            reportFile.ReportType,
            reportFile.Format,
            reportFile.FileName,
            reportFile.ContentType,
            reportFile.ByteLength,
            reportFile.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<ReportJobView>> ListReportJobs(Guid tenantId, CancellationToken ct)
    {
        return await db.ReportJobs
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => ToView(x))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReportFileView>> ListReportFiles(Guid tenantId, CancellationToken ct)
    {
        return await db.ReportFiles
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
            .ToListAsync(ct);
    }

    public Task<ReportFile?> GetReportFile(Guid tenantId, Guid reportFileId, CancellationToken ct)
    {
        return db.ReportFiles.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == reportFileId, ct);
    }

    public async Task CompleteReportJob(ReportJob job, byte[] content, string fileName, string contentType, CancellationToken ct)
    {
        var reportFile = new ReportFile
        {
            TenantId = job.TenantId,
            ReportType = job.ReportType,
            Format = job.Format,
            FileName = fileName,
            ContentType = contentType,
            ByteLength = content.LongLength,
            Content = content,
            CreatedByUserAccountId = job.RequestedByUserAccountId
        };

        db.ReportFiles.Add(reportFile);
        await db.SaveChangesAsync(ct);

        job.ReportFileId = reportFile.Id;
        job.Status = ReportJobStatuses.Completed;
        job.CompletedAtUtc = SystemClock.Instance.GetCurrentInstant();
        job.FailureReason = null;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Reports(job.TenantId)], ct);
    }

    public async Task FailReportJob(ReportJob job, string reason, CancellationToken ct)
    {
        job.Status = ReportJobStatuses.Failed;
        job.FailureReason = reason;
        job.CompletedAtUtc = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(ct);
        await cache.InvalidateTagsAsync([ApiCacheTags.Reports(job.TenantId)], ct);
    }

    private async Task<(byte[] Content, string FileName, string ContentType)> BuildInventoryDocument(Guid tenantId, ExportKind exportKind, string stamp, CancellationToken ct)
    {
        var report = await BuildInventoryReport(tenantId, ct);
        return exportKind == ExportKind.Pdf
            ? (renderer.RenderSimplePdf("INVENTORY REPORT", InventoryPdfLines(report)), $"inventory-report-{stamp}.pdf", "application/pdf")
            : (renderer.RenderSpreadsheet(InventoryCsvRows(report), "Inventory"), $"inventory-report-{stamp}.xlsx", ReportDocumentRenderer.XlsxMimeType);
    }

    private async Task<(byte[] Content, string FileName, string ContentType)> BuildSalesDocument(Guid tenantId, ExportKind exportKind, string stamp, Instant? fromUtc, Instant? toUtc, CancellationToken ct)
    {
        var range = NormalizeDateRange(fromUtc, toUtc);
        var report = await BuildSalesReport(tenantId, range.FromUtc, range.ToUtc, ct);
        return exportKind == ExportKind.Pdf
            ? (renderer.RenderSimplePdf("SALES REPORT", SalesPdfLines(report)), $"sales-report-{stamp}.pdf", "application/pdf")
            : (renderer.RenderSpreadsheet(SalesCsvRows(report), "Sales"), $"sales-report-{stamp}.xlsx", ReportDocumentRenderer.XlsxMimeType);
    }

    private async Task<(byte[] Content, string FileName, string ContentType)> BuildProfitLossDocument(Guid tenantId, ExportKind exportKind, string stamp, Instant? fromUtc, Instant? toUtc, CancellationToken ct)
    {
        var range = NormalizeDateRange(fromUtc, toUtc);
        var report = await BuildProfitLossReport(tenantId, range.FromUtc, range.ToUtc, ct);
        return exportKind == ExportKind.Pdf
            ? (renderer.RenderSimplePdf("PROFIT LOSS REPORT", ProfitLossPdfLines(report)), $"profit-loss-report-{stamp}.pdf", "application/pdf")
            : (renderer.RenderSpreadsheet(ProfitLossCsvRows(report), "ProfitLoss"), $"profit-loss-report-{stamp}.xlsx", ReportDocumentRenderer.XlsxMimeType);
    }

    private async Task<(byte[] Content, string FileName, string ContentType)> BuildCreditorsDocument(Guid tenantId, ExportKind exportKind, string stamp, Instant? fromUtc, Instant? toUtc, CancellationToken ct)
    {
        var report = await BuildCreditorsReport(tenantId, fromUtc, toUtc, ct);
        return exportKind == ExportKind.Pdf
            ? (renderer.RenderSimplePdf("CREDITORS REPORT", CreditorsPdfLines(report)), $"creditors-report-{stamp}.pdf", "application/pdf")
            : (renderer.RenderSpreadsheet(CreditorsCsvRows(report), "Creditors"), $"creditors-report-{stamp}.xlsx", ReportDocumentRenderer.XlsxMimeType);
    }

    private static (Instant FromUtc, Instant ToUtc) NormalizeDateRange(Instant? fromUtc, Instant? toUtc)
    {
        var to = toUtc ?? SystemClock.Instance.GetCurrentInstant();
        var from = fromUtc ?? to - Duration.FromDays(30);
        return (from, to);
    }

    private static ReportFilterEnvelope ParseFilter(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson))
        {
            return new ReportFilterEnvelope(null, null);
        }

        return JsonSerializer.Deserialize<ReportFilterEnvelope>(filterJson) ?? new ReportFilterEnvelope(null, null);
    }

    private static ReportJobView ToView(ReportJob job)
    {
        return new ReportJobView(
            job.Id,
            job.ReportType,
            job.Format,
            job.Status,
            job.FilterJson,
            job.ReportFileId,
            job.RequestedAtUtc,
            job.CompletedAtUtc,
            job.FailureReason);
    }

    public static IEnumerable<string[]> InventoryCsvRows(InventoryReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("g", null)];
        yield return ["TotalProducts", report.TotalProducts.ToString()];
        yield return ["TotalUnits", report.TotalUnits.ToString()];
        yield return ["LowStockItems", report.LowStockItems.ToString()];
        yield return ["TotalCostValue", report.TotalCostValue.ToString("0.00")];
        yield return ["TotalSellingValue", report.TotalSellingValue.ToString("0.00")];
        yield return [];
        yield return ["ItemId", "ProductName", "Quantity", "CostPrice", "SellingPrice", "CostValue", "SellingValue", "ExpiryDate"];
        foreach (var row in report.Items)
        {
            yield return
            [
                row.ItemId.ToString(),
                row.ProductName,
                row.Quantity.ToString(),
                row.CostPrice.ToString("0.00"),
                row.SellingPrice.ToString("0.00"),
                row.CostValue.ToString("0.00"),
                row.SellingValue.ToString("0.00"),
                row.ExpiryDate?.ToString("uuuu-MM-dd", null) ?? ""
            ];
        }
    }

    public static IEnumerable<string> InventoryPdfLines(InventoryReportResponse report)
    {
        yield return $"Products: {report.TotalProducts}";
        yield return $"Units: {report.TotalUnits}";
        yield return $"Low Stock: {report.LowStockItems}";
        yield return $"Cost Value: NGN {report.TotalCostValue:0.00}";
        yield return $"Selling Value: NGN {report.TotalSellingValue:0.00}";
        yield return string.Empty;
        foreach (var item in report.Items.Take(50))
        {
            yield return $"{item.ProductName} | Qty:{item.Quantity} | Cost:{item.CostPrice:0.00} | Sell:{item.SellingPrice:0.00}";
        }
    }

    public static IEnumerable<string[]> SalesCsvRows(SalesReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("g", null)];
        yield return ["FromUtc", report.FromUtc.ToString("g", null)];
        yield return ["ToUtc", report.ToUtc.ToString("g", null)];
        yield return ["SalesCount", report.SalesCount.ToString()];
        yield return ["Revenue", report.Revenue.ToString("0.00")];
        yield return ["VatAmount", report.VatAmount.ToString("0.00")];
        yield return ["DiscountAmount", report.DiscountAmount.ToString("0.00")];
        yield return ["OutstandingAmount", report.OutstandingAmount.ToString("0.00")];
        yield return [];
        yield return ["DailyDate", "SalesCount", "Revenue", "Outstanding"];
        foreach (var day in report.Daily)
        {
            yield return [day.Date.ToString("uuuu-MM-dd", null), day.SalesCount.ToString(), day.Revenue.ToString("0.00"), day.Outstanding.ToString("0.00")];
        }
        yield return [];
        yield return ["PaymentMethod", "Amount"];
        foreach (var payment in report.Payments)
        {
            yield return [payment.Method, payment.Amount.ToString("0.00")];
        }
    }

    public static IEnumerable<string> SalesPdfLines(SalesReportResponse report)
    {
        yield return $"Range: {report.FromUtc.InUtc().Date.ToString("uuuu-MM-dd", null)} to {report.ToUtc.InUtc().Date.ToString("uuuu-MM-dd", null)}";
        yield return $"Sales Count: {report.SalesCount}";
        yield return $"Revenue: NGN {report.Revenue:0.00}";
        yield return $"VAT: NGN {report.VatAmount:0.00}";
        yield return $"Discount: NGN {report.DiscountAmount:0.00}";
        yield return $"Outstanding: NGN {report.OutstandingAmount:0.00}";
        yield return string.Empty;
        foreach (var day in report.Daily.Take(40))
        {
            yield return $"{day.Date.ToString("uuuu-MM-dd", null)} | Sales:{day.SalesCount} | Revenue:{day.Revenue:0.00} | Outstanding:{day.Outstanding:0.00}";
        }
    }

    public static IEnumerable<string[]> ProfitLossCsvRows(ProfitLossReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("g", null)];
        yield return ["FromUtc", report.FromUtc.ToString("g", null)];
        yield return ["ToUtc", report.ToUtc.ToString("g", null)];
        yield return ["Revenue", report.Revenue.ToString("0.00")];
        yield return ["COGS", report.Cogs.ToString("0.00")];
        yield return ["GrossProfit", report.GrossProfit.ToString("0.00")];
        yield return ["Expenses", report.Expenses.ToString("0.00")];
        yield return ["NetProfitLoss", report.NetProfitLoss.ToString("0.00")];
        yield return [];
        yield return ["Category", "Amount"];
        foreach (var row in report.ExpenseBreakdown)
        {
            yield return [row.Category, row.Amount.ToString("0.00")];
        }
    }

    public static IEnumerable<string> ProfitLossPdfLines(ProfitLossReportResponse report)
    {
        yield return $"Range: {report.FromUtc.InUtc().Date.ToString("uuuu-MM-dd", null)} to {report.ToUtc.InUtc().Date.ToString("uuuu-MM-dd", null)}";
        yield return $"Revenue: NGN {report.Revenue:0.00}";
        yield return $"COGS: NGN {report.Cogs:0.00}";
        yield return $"Gross Profit: NGN {report.GrossProfit:0.00}";
        yield return $"Expenses: NGN {report.Expenses:0.00}";
        yield return $"Net Profit/Loss: NGN {report.NetProfitLoss:0.00}";
        if (report.ExpenseBreakdown.Count > 0)
        {
            yield return string.Empty;
            foreach (var row in report.ExpenseBreakdown.Take(20))
            {
                yield return $"{row.Category}: NGN {row.Amount:0.00}";
            }
        }
    }

    public static IEnumerable<string[]> CreditorsCsvRows(CreditorsReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("g", null)];
        yield return ["FromUtc", report.FromUtc?.ToString("g", null) ?? ""];
        yield return ["ToUtc", report.ToUtc?.ToString("g", null) ?? ""];
        yield return ["OpenCredits", report.OpenCredits.ToString()];
        yield return ["TotalOutstanding", report.TotalOutstanding.ToString("0.00")];
        yield return [];
        yield return ["CreditAccountId", "SaleNumber", "CustomerName", "ItemsSummary", "DueDateUtc", "DaysOverdue", "OutstandingAmount", "Status"];
        foreach (var row in report.Credits)
        {
            yield return
            [
                row.CreditAccountId.ToString(),
                row.SaleNumber,
                row.CustomerName,
                row.ItemsSummary,
                row.DueDateUtc.ToString("g", null),
                row.DaysOverdue.ToString(),
                row.OutstandingAmount.ToString("0.00"),
                row.Status
            ];
        }
    }

    public static IEnumerable<string> CreditorsPdfLines(CreditorsReportResponse report)
    {
        yield return $"Open Credits: {report.OpenCredits}";
        yield return $"Total Outstanding: NGN {report.TotalOutstanding:0.00}";
        yield return string.Empty;
        foreach (var row in report.Credits.Take(50))
        {
            yield return $"{row.CustomerName} | {row.SaleNumber} | Due:{row.DueDateUtc.InUtc().Date.ToString("uuuu-MM-dd", null)} | Out:{row.OutstandingAmount:0.00}";
        }
    }

    private static ExpenseView ToView(Expense expense)
    {
        return new ExpenseView(
            expense.Id,
            expense.Title,
            expense.Category,
            expense.Amount,
            expense.ExpenseDateUtc,
            expense.Notes,
            expense.CreatedAtUtc,
            Convert.ToBase64String(expense.RowVersion));
    }

    public static class ReportJobStatuses
    {
        public const string Pending = "Pending";
        public const string InProgress = "InProgress";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
    }

    public enum ExportKind
    {
        Pdf,
        Spreadsheet
    }

    private sealed record ReportFilterEnvelope(Instant? FromUtc, Instant? ToUtc);
}
