using NodaTime;

namespace Shopkeeper.Api.Contracts;

public sealed record InventoryReportRow(
    Guid ItemId,
    string ProductName,
    int Quantity,
    decimal CostPrice,
    decimal SellingPrice,
    decimal CostValue,
    decimal SellingValue,
    LocalDate? ExpiryDate);

public sealed record InventoryReportResponse(
    Instant GeneratedAtUtc,
    int TotalProducts,
    int TotalUnits,
    int LowStockItems,
    decimal TotalCostValue,
    decimal TotalSellingValue,
    IReadOnlyList<InventoryReportRow> Items);

public sealed record SalesDailySummaryRow(
    LocalDate Date,
    int SalesCount,
    decimal Revenue,
    decimal Outstanding);

public sealed record SalesPaymentSummaryRow(
    string Method,
    decimal Amount);

public sealed record SalesReportResponse(
    Instant GeneratedAtUtc,
    Instant FromUtc,
    Instant ToUtc,
    int SalesCount,
    decimal Revenue,
    decimal VatAmount,
    decimal DiscountAmount,
    decimal OutstandingAmount,
    IReadOnlyList<SalesDailySummaryRow> Daily,
    IReadOnlyList<SalesPaymentSummaryRow> Payments);

public sealed record ProfitLossReportResponse(
    Instant GeneratedAtUtc,
    Instant FromUtc,
    Instant ToUtc,
    decimal Revenue,
    decimal Cogs,
    decimal GrossProfit,
    decimal Expenses,
    decimal NetProfitLoss,
    IReadOnlyList<ExpenseCategorySummaryRow> ExpenseBreakdown);

public sealed record ExpenseCategorySummaryRow(
    string Category,
    decimal Amount);

public sealed record CreditorReportRow(
    Guid CreditAccountId,
    Guid SaleId,
    string SaleNumber,
    string CustomerName,
    string ItemsSummary,
    Instant DueDateUtc,
    int DaysOverdue,
    decimal OutstandingAmount,
    string Status);

public sealed record CreditorsReportResponse(
    Instant GeneratedAtUtc,
    Instant? FromUtc,
    Instant? ToUtc,
    int OpenCredits,
    decimal TotalOutstanding,
    IReadOnlyList<CreditorReportRow> Credits);

public sealed record ReportJobView(
    Guid Id,
    string ReportType,
    string Format,
    string Status,
    string? FilterJson,
    Guid? ReportFileId,
    Instant RequestedAtUtc,
    Instant? CompletedAtUtc,
    string? FailureReason);

public sealed record ReportFileView(
    Guid Id,
    string ReportType,
    string Format,
    string FileName,
    string ContentType,
    long ByteLength,
    Instant CreatedAtUtc);

public sealed record QueueReportJobRequest(
    string ReportType,
    string Format,
    Instant? FromUtc,
    Instant? ToUtc);
