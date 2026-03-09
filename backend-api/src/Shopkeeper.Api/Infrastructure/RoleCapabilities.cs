using System.Security.Claims;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Infrastructure;

public static class RoleCapabilities
{
    public static MembershipRole ParseRole(string? value)
    {
        // Accept legacy "Staff" alias from older clients — always maps to ShopManager.
        if (value?.Equals("Staff", StringComparison.OrdinalIgnoreCase) == true)
        {
            return MembershipRole.ShopManager;
        }

        if (Enum.TryParse<MembershipRole>(value, true, out var parsed))
        {
            return parsed;
        }

        return MembershipRole.Salesperson;
    }

    public static MembershipRole? GetRole(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(CustomClaimTypes.Role);
        return string.IsNullOrWhiteSpace(value) ? null : ParseRole(value);
    }

    public static bool IsOwner(MembershipRole role) => role == MembershipRole.Owner;

    public static bool IsManagerOrOwner(MembershipRole role) =>
        role == MembershipRole.Owner || role == MembershipRole.ShopManager;

    public static bool CanManageInventory(MembershipRole role) => IsManagerOrOwner(role);

    public static bool CanManageSales(MembershipRole role) =>
        role == MembershipRole.Owner || role == MembershipRole.ShopManager || role == MembershipRole.Salesperson;

    public static bool CanVoidSales(MembershipRole role) => IsManagerOrOwner(role);

    public static bool CanViewReports(MembershipRole role) => IsManagerOrOwner(role);

    public static bool CanManageExpenses(MembershipRole role) => IsOwner(role);

    public static bool CanManageShopSettings(MembershipRole role) => IsOwner(role);

    public static bool CanManageStaff(MembershipRole role) => IsOwner(role);
}
