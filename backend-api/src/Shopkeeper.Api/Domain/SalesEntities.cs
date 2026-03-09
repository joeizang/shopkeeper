namespace Shopkeeper.Api.Domain;

public sealed class Sale : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string SaleNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsCredit { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public bool IsVoided { get; set; }
    public Guid CreatedByMembershipId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<SaleLine> Lines { get; set; } = [];
    public ICollection<SalePayment> Payments { get; set; } = [];
    public CreditAccount? CreditAccount { get; set; }
}

public sealed class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public Guid InventoryItemId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal CostPriceSnapshot { get; set; }
    public decimal LineTotal { get; set; }

    public Sale Sale { get; set; } = default!;
}

public sealed class SalePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Sale Sale { get; set; } = default!;
}

public sealed class CreditAccount : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public DateTime DueDateUtc { get; set; }
    public decimal OutstandingAmount { get; set; }
    public CreditStatus Status { get; set; } = CreditStatus.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Sale Sale { get; set; } = default!;
    public ICollection<CreditRepayment> Repayments { get; set; } = [];
}

public sealed class CreditRepayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CreditAccountId { get; set; }
    public Guid SalePaymentId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public CreditAccount CreditAccount { get; set; } = default!;
    public SalePayment SalePayment { get; set; } = default!;
}
