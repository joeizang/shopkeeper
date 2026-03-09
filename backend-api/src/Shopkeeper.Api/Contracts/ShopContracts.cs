using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Contracts;

public sealed record CreateShopRequest(string Name, bool VatEnabled, decimal VatRate);
public sealed record UpdateShopVatSettingsRequest(bool VatEnabled, decimal VatRate, decimal DefaultDiscountPercent, string? RowVersionBase64);

public sealed record InviteStaffRequest(string FullName, string? Email, string? Phone, string TemporaryPassword, string Role);
public sealed record UpdateStaffMembershipRequest(string Role, bool IsActive);
public sealed record StaffMembershipView(Guid StaffId, Guid UserId, string FullName, string? Email, string? Phone, string Role, bool IsActive, DateTime CreatedAtUtc);

public sealed record ShopView(Guid Id, string Name, string Code, bool VatEnabled, decimal VatRate, decimal DefaultDiscountPercent, string Role, string RowVersionBase64);
