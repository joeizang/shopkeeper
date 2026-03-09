using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Contracts;

public sealed record SaleLineRequest(Guid InventoryItemId, int Quantity, decimal UnitPrice);

public sealed record SalePaymentRequest(PaymentMethod Method, decimal Amount, string? Reference);

public sealed record CreateSaleRequest(
    string? CustomerName,
    string? CustomerPhone,
    decimal DiscountAmount,
    bool IsCredit,
    DateTime? DueDateUtc,
    List<SaleLineRequest> Lines,
    List<SalePaymentRequest>? InitialPayments);

public sealed record AddSalePaymentRequest(PaymentMethod Method, decimal Amount, string? Reference, string? Note);

public sealed record SaleLineView(Guid Id, Guid InventoryItemId, string ProductNameSnapshot, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record SalePaymentView(Guid Id, string Method, decimal Amount, string? Reference, DateTime CreatedAtUtc);

public sealed record SaleDetailResponse(
    Guid Id,
    string SaleNumber,
    string? CustomerName,
    string? CustomerPhone,
    decimal Subtotal,
    decimal VatAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal OutstandingAmount,
    string Status,
    bool IsCredit,
    DateTime? DueDateUtc,
    bool IsVoided,
    DateTime UpdatedAtUtc,
    IReadOnlyList<SaleLineView> Lines,
    IReadOnlyList<SalePaymentView> Payments);

public sealed record ReceiptLineView(string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record ReceiptView(
    Guid SaleId,
    string SaleNumber,
    DateTime CreatedAtUtc,
    string ShopName,
    string? CustomerName,
    decimal Subtotal,
    decimal VatAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    List<ReceiptLineView> Lines,
    List<SalePaymentRequest> Payments);
