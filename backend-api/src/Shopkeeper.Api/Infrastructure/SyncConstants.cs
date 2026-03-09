namespace Shopkeeper.Api.Infrastructure;

internal static class SyncConstants
{
    /// <summary>
    /// Device ID stamped on SyncChange and AuditLog records created by server-side API writes.
    /// </summary>
    public const string ServerDeviceId = "server-api";
}
