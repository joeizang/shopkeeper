using NodaTime;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Contracts;

public sealed record CreateInventoryItemRequest(
    string ProductName,
    string? ModelNumber,
    string? SerialNumber,
    int Quantity,
    LocalDate? ExpiryDate,
    decimal CostPrice,
    decimal SellingPrice,
    ItemType ItemType,
    ItemConditionGrade? ConditionGrade,
    string? ConditionNotes);

public sealed record UpdateInventoryItemRequest(
    string? ProductName,
    string? ModelNumber,
    string? SerialNumber,
    int? Quantity,
    LocalDate? ExpiryDate,
    decimal? CostPrice,
    decimal? SellingPrice,
    ItemType? ItemType,
    ItemConditionGrade? ConditionGrade,
    string? ConditionNotes,
    string? RowVersionBase64);

public sealed record AddItemPhotoRequest(string PhotoUri);

public sealed record StockAdjustmentRequest(Guid InventoryItemId, int DeltaQuantity, string Reason);

public sealed record InventoryItemView(
    Guid Id,
    string ProductName,
    string? ModelNumber,
    string? SerialNumber,
    int Quantity,
    LocalDate? ExpiryDate,
    decimal CostPrice,
    decimal SellingPrice,
    ItemType ItemType,
    ItemConditionGrade? ConditionGrade,
    string? ConditionNotes,
    List<string> PhotoUris,
    string RowVersionBase64);
