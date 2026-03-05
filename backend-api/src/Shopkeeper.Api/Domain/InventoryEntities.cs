namespace Shopkeeper.Api.Domain;

public sealed class InventoryItem : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ModelNumber { get; set; }
    public string? SerialNumber { get; set; }
    public int Quantity { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public ItemType ItemType { get; set; } = ItemType.New;
    public ItemConditionGrade? ConditionGrade { get; set; }
    public string? ConditionNotes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ItemPhoto> Photos { get; set; } = new List<ItemPhoto>();
}

public sealed class ItemPhoto : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InventoryItemId { get; set; }
    public string PhotoUri { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public InventoryItem InventoryItem { get; set; } = default!;
}

public sealed class StockAdjustment : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int DeltaQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? CreatedByMembershipId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public InventoryItem InventoryItem { get; set; } = default!;
}
