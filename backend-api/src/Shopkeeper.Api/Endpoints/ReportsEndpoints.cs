using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class ReportsEndpoints
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.StaffOrOwner });

        group.MapGet("/inventory", GetInventoryReport);
        group.MapGet("/sales", GetSalesReport);
        group.MapGet("/profit-loss", GetProfitLossReport)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });
        group.MapGet("/creditors", GetCreditorsReport);
        group.MapGet("/{reportType}/export", ExportReport);

        return app;
    }

    private static async Task<IResult> GetInventoryReport(
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var report = await reporting.BuildInventoryReport(tenantId.Value, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetSalesReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var range = ResolveDateRange(from, to);
        if (range.error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
        }

        var report = await reporting.BuildSalesReport(tenantId.Value, range.fromUtc, range.toUtc, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetProfitLossReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var range = ResolveDateRange(from, to);
        if (range.error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
        }

        var report = await reporting.BuildProfitLossReport(tenantId.Value, range.fromUtc, range.toUtc, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetCreditorsReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var fromUtc = ParseDateStart(from);
        var toUtc = ParseDateEnd(to);
        if ((from is not null && !fromUtc.HasValue) || (to is not null && !toUtc.HasValue))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["Invalid date format. Use YYYY-MM-DD."] });
        }

        var report = await reporting.BuildCreditorsReport(tenantId.Value, fromUtc, toUtc, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> ExportReport(
        string reportType,
        [FromQuery] string? format,
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        ReportDocumentRenderer renderer,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var exportKind = NormalizeFormat(format);
        if (exportKind is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["format"] = ["Use pdf or spreadsheet."] });
        }

        var normalizedType = reportType.Trim().ToLowerInvariant();

        if (normalizedType == "profit-loss" &&
            !httpContext.User.HasClaim(c => c.Type == CustomClaimTypes.Role && c.Value == MembershipRole.Owner.ToString()))
        {
            return Results.Forbid();
        }

        switch (normalizedType)
        {
            case "inventory":
            {
                var report = await reporting.BuildInventoryReport(tenantId.Value, ct);
                return BuildExportResponse(
                    exportKind.Value,
                    renderer,
                    "inventory",
                    InventoryCsvRows(report),
                    InventoryPdfLines(report));
            }
            case "sales":
            {
                var range = ResolveDateRange(from, to);
                if (range.error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
                }
                var report = await reporting.BuildSalesReport(tenantId.Value, range.fromUtc, range.toUtc, ct);
                return BuildExportResponse(
                    exportKind.Value,
                    renderer,
                    "sales",
                    SalesCsvRows(report),
                    SalesPdfLines(report));
            }
            case "profit-loss":
            {
                var range = ResolveDateRange(from, to);
                if (range.error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
                }
                var report = await reporting.BuildProfitLossReport(tenantId.Value, range.fromUtc, range.toUtc, ct);
                return BuildExportResponse(
                    exportKind.Value,
                    renderer,
                    "profit-loss",
                    ProfitLossCsvRows(report),
                    ProfitLossPdfLines(report));
            }
            case "creditors":
            {
                var fromUtc = ParseDateStart(from);
                var toUtc = ParseDateEnd(to);
                if ((from is not null && !fromUtc.HasValue) || (to is not null && !toUtc.HasValue))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["Invalid date format. Use YYYY-MM-DD."] });
                }
                var report = await reporting.BuildCreditorsReport(tenantId.Value, fromUtc, toUtc, ct);
                return BuildExportResponse(
                    exportKind.Value,
                    renderer,
                    "creditors",
                    CreditorsCsvRows(report),
                    CreditorsPdfLines(report));
            }
            default:
                return Results.NotFound(new { message = $"Unknown report type '{reportType}'." });
        }
    }

    private static IResult BuildExportResponse(
        ExportKind exportKind,
        ReportDocumentRenderer renderer,
        string reportType,
        IEnumerable<string[]> csvRows,
        IEnumerable<string> pdfLines)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        if (exportKind == ExportKind.Pdf)
        {
            var bytes = renderer.RenderSimplePdf($"{reportType.ToUpperInvariant()} REPORT", pdfLines);
            return Results.File(bytes, "application/pdf", $"{reportType}-report-{stamp}.pdf");
        }

        var csv = renderer.RenderCsv(csvRows);
        return Results.File(csv, "text/csv", $"{reportType}-report-{stamp}.csv");
    }

    private static IEnumerable<string[]> InventoryCsvRows(InventoryReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("u")];
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
                row.ExpiryDate?.ToString("yyyy-MM-dd") ?? ""
            ];
        }
    }

    private static IEnumerable<string> InventoryPdfLines(InventoryReportResponse report)
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

    private static IEnumerable<string[]> SalesCsvRows(SalesReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("u")];
        yield return ["FromUtc", report.FromUtc.ToString("u")];
        yield return ["ToUtc", report.ToUtc.ToString("u")];
        yield return ["SalesCount", report.SalesCount.ToString()];
        yield return ["Revenue", report.Revenue.ToString("0.00")];
        yield return ["VatAmount", report.VatAmount.ToString("0.00")];
        yield return ["DiscountAmount", report.DiscountAmount.ToString("0.00")];
        yield return ["OutstandingAmount", report.OutstandingAmount.ToString("0.00")];
        yield return [];
        yield return ["DailyDate", "SalesCount", "Revenue", "Outstanding"];
        foreach (var day in report.Daily)
        {
            yield return [day.Date.ToString("yyyy-MM-dd"), day.SalesCount.ToString(), day.Revenue.ToString("0.00"), day.Outstanding.ToString("0.00")];
        }
        yield return [];
        yield return ["PaymentMethod", "Amount"];
        foreach (var payment in report.Payments)
        {
            yield return [payment.Method, payment.Amount.ToString("0.00")];
        }
    }

    private static IEnumerable<string> SalesPdfLines(SalesReportResponse report)
    {
        yield return $"Range: {report.FromUtc:yyyy-MM-dd} to {report.ToUtc:yyyy-MM-dd}";
        yield return $"Sales Count: {report.SalesCount}";
        yield return $"Revenue: NGN {report.Revenue:0.00}";
        yield return $"VAT: NGN {report.VatAmount:0.00}";
        yield return $"Discount: NGN {report.DiscountAmount:0.00}";
        yield return $"Outstanding: NGN {report.OutstandingAmount:0.00}";
        yield return string.Empty;
        foreach (var day in report.Daily.Take(40))
        {
            yield return $"{day.Date:yyyy-MM-dd} | Sales:{day.SalesCount} | Revenue:{day.Revenue:0.00} | Outstanding:{day.Outstanding:0.00}";
        }
    }

    private static IEnumerable<string[]> ProfitLossCsvRows(ProfitLossReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("u")];
        yield return ["FromUtc", report.FromUtc.ToString("u")];
        yield return ["ToUtc", report.ToUtc.ToString("u")];
        yield return ["Revenue", report.Revenue.ToString("0.00")];
        yield return ["COGS", report.Cogs.ToString("0.00")];
        yield return ["GrossProfit", report.GrossProfit.ToString("0.00")];
        yield return ["Expenses", report.Expenses.ToString("0.00")];
        yield return ["NetProfitLoss", report.NetProfitLoss.ToString("0.00")];
    }

    private static IEnumerable<string> ProfitLossPdfLines(ProfitLossReportResponse report)
    {
        yield return $"Range: {report.FromUtc:yyyy-MM-dd} to {report.ToUtc:yyyy-MM-dd}";
        yield return $"Revenue: NGN {report.Revenue:0.00}";
        yield return $"COGS: NGN {report.Cogs:0.00}";
        yield return $"Gross Profit: NGN {report.GrossProfit:0.00}";
        yield return $"Expenses: NGN {report.Expenses:0.00}";
        yield return $"Net Profit/Loss: NGN {report.NetProfitLoss:0.00}";
    }

    private static IEnumerable<string[]> CreditorsCsvRows(CreditorsReportResponse report)
    {
        yield return ["GeneratedAtUtc", report.GeneratedAtUtc.ToString("u")];
        yield return ["FromUtc", report.FromUtc?.ToString("u") ?? ""];
        yield return ["ToUtc", report.ToUtc?.ToString("u") ?? ""];
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
                row.DueDateUtc.ToString("u"),
                row.DaysOverdue.ToString(),
                row.OutstandingAmount.ToString("0.00"),
                row.Status
            ];
        }
    }

    private static IEnumerable<string> CreditorsPdfLines(CreditorsReportResponse report)
    {
        yield return $"Open Credits: {report.OpenCredits}";
        yield return $"Total Outstanding: NGN {report.TotalOutstanding:0.00}";
        yield return string.Empty;
        foreach (var row in report.Credits.Take(50))
        {
            yield return $"{row.CustomerName} | {row.SaleNumber} | Due:{row.DueDateUtc:yyyy-MM-dd} | Out:{row.OutstandingAmount:0.00}";
        }
    }

    private static (DateTime fromUtc, DateTime toUtc, string? error) ResolveDateRange(string? fromRaw, string? toRaw)
    {
        var defaultFrom = DateTime.UtcNow.Date.AddDays(-30);
        var defaultTo = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
        var from = ParseDateStart(fromRaw) ?? defaultFrom;
        var to = ParseDateEnd(toRaw) ?? defaultTo;
        if (from > to)
        {
            return (defaultFrom, defaultTo, "'from' date must be before or equal to 'to' date.");
        }

        return (from, to, null);
    }

    private static DateTime? ParseDateStart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParse(value, out var dateOnly))
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }
        return null;
    }

    private static DateTime? ParseDateEnd(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParse(value, out var dateOnly))
        {
            return dateOnly.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        }
        return null;
    }

    private static ExportKind? NormalizeFormat(string? format)
    {
        return format?.Trim().ToLowerInvariant() switch
        {
            "pdf" => ExportKind.Pdf,
            "spreadsheet" => ExportKind.Spreadsheet,
            "csv" => ExportKind.Spreadsheet,
            "xlsx" => ExportKind.Spreadsheet,
            _ => null
        };
    }

    private enum ExportKind
    {
        Pdf,
        Spreadsheet
    }
}
