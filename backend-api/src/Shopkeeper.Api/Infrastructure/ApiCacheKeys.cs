using NodaTime;

namespace Shopkeeper.Api.Infrastructure;

public static class ApiCacheKeys
{
    public static string AccountProfile(Guid userId) => $"user:{userId}:account:profile";
    public static string UserShops(Guid userId) => $"user:{userId}:shops";
    public static string StaffList(Guid tenantId) => $"tenant:{tenantId}:staff:list";
    public static string InventoryList(Guid tenantId, int page, int limit) => $"tenant:{tenantId}:inventory:list:{page}:{limit}";
    public static string InventoryItem(Guid tenantId, Guid itemId) => $"tenant:{tenantId}:inventory:item:{itemId}";
    public static string CreditList(Guid tenantId, int page, int limit) => $"tenant:{tenantId}:credits:list:{page}:{limit}";
    public static string CreditDetail(Guid tenantId, Guid saleId) => $"tenant:{tenantId}:credits:detail:{saleId}";
    public static string ExpenseList(Guid tenantId, Instant? fromUtc, Instant? toUtc)
        => $"tenant:{tenantId}:expenses:{FormatInstant(fromUtc)}:{FormatInstant(toUtc)}";
    public static string InventoryReport(Guid tenantId) => $"tenant:{tenantId}:reports:inventory";
    public static string SalesReport(Guid tenantId, Instant fromUtc, Instant toUtc)
        => $"tenant:{tenantId}:reports:sales:{fromUtc}:{toUtc}";
    public static string ProfitLossReport(Guid tenantId, Instant fromUtc, Instant toUtc)
        => $"tenant:{tenantId}:reports:profit-loss:{fromUtc}:{toUtc}";
    public static string CreditorsReport(Guid tenantId, Instant? fromUtc, Instant? toUtc)
        => $"tenant:{tenantId}:reports:creditors:{FormatInstant(fromUtc)}:{FormatInstant(toUtc)}";
    public static string ReportJobs(Guid tenantId) => $"tenant:{tenantId}:reports:jobs";
    public static string ReportJob(Guid tenantId, Guid reportJobId) => $"tenant:{tenantId}:reports:jobs:{reportJobId}";
    public static string ReportFiles(Guid tenantId) => $"tenant:{tenantId}:reports:files";

    private static string FormatInstant(Instant? value) => value?.ToString("g", null) ?? "none";
}
