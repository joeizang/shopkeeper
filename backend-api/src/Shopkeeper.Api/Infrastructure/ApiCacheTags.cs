namespace Shopkeeper.Api.Infrastructure;

public static class ApiCacheTags
{
    public static string Tenant(Guid tenantId) => $"tenant:{tenantId}";
    public static string UserAccount(Guid userId) => $"user:{userId}:account";
    public static string UserShops(Guid userId) => $"user:{userId}:shops";
    public static string Staff(Guid tenantId) => $"tenant:{tenantId}:staff";
    public static string Inventory(Guid tenantId) => $"tenant:{tenantId}:inventory";
    public static string Sales(Guid tenantId) => $"tenant:{tenantId}:sales";
    public static string Credits(Guid tenantId) => $"tenant:{tenantId}:credits";
    public static string Expenses(Guid tenantId) => $"tenant:{tenantId}:expenses";
    public static string Reports(Guid tenantId) => $"tenant:{tenantId}:reports";
}
