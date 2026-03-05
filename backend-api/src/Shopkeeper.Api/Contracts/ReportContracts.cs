namespace Shopkeeper.Api.Contracts;

public sealed record InventoryReportRow(
    Guid ItemId,
    string ProductName,
    int Quantity,
    decimal CostPrice,
    decimal SellingPrice,
    decimal CostValue,
    decimal SellingValue,
    DateOnly? ExpiryDate);

public sealed record InventoryReportResponse(
    DateTime GeneratedAtUtc,
    int TotalProducts,
    int TotalUnits,
    int LowStockItems,
    decimal TotalCostValue,
    decimal TotalSellingValue,
    IReadOnlyList<InventoryReportRow> Items);

public sealed record SalesDailySummaryRow(
    DateOnly Date,
    int SalesCount,
    decimal Revenue,
    decimal Outstanding);

public sealed record SalesPaymentSummaryRow(
    string Method,
    decimal Amount);

public sealed record SalesReportResponse(
    DateTime GeneratedAtUtc,
    DateTime FromUtc,
    DateTime ToUtc,
    int SalesCount,
    decimal Revenue,
    decimal VatAmount,
    decimal DiscountAmount,
    decimal OutstandingAmount,
    IReadOnlyList<SalesDailySummaryRow> Daily,
    IReadOnlyList<SalesPaymentSummaryRow> Payments);

public sealed record ProfitLossReportResponse(
    DateTime GeneratedAtUtc,
    DateTime FromUtc,
    DateTime ToUtc,
    decimal Revenue,
    decimal Cogs,
    decimal GrossProfit,
    decimal Expenses,
    decimal NetProfitLoss);

public sealed record CreditorReportRow(
    Guid CreditAccountId,
    Guid SaleId,
    string SaleNumber,
    string CustomerName,
    string ItemsSummary,
    DateTime DueDateUtc,
    int DaysOverdue,
    decimal OutstandingAmount,
    string Status);

public sealed record CreditorsReportResponse(
    DateTime GeneratedAtUtc,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int OpenCredits,
    decimal TotalOutstanding,
    IReadOnlyList<CreditorReportRow> Credits);
