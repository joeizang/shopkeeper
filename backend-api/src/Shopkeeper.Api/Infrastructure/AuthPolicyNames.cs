namespace Shopkeeper.Api.Infrastructure;

public static class AuthPolicyNames
{
    public const string OwnerOnly = "OwnerOnly";
    public const string OwnerOrManager = "OwnerOrManager";
    public const string SalesAccess = "SalesAccess";
    public const string ReportingAccess = "ReportingAccess";
}
