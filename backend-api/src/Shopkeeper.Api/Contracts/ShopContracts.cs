using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Contracts;

public sealed record CreateShopRequest(string Name, bool VatEnabled, decimal VatRate);

public sealed record InviteStaffRequest(string FullName, string? Email, string? Phone, string TemporaryPassword);

public sealed record ShopView(Guid Id, string Name, string Code, bool VatEnabled, decimal VatRate, MembershipRole Role);
