namespace Shopkeeper.Api.Domain;

public sealed class SyncChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public SyncOperation Operation { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime ClientUpdatedAtUtc { get; set; }
    public DateTime ServerUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public SyncStatus Status { get; set; } = SyncStatus.Accepted;
    public string? ConflictReason { get; set; }
}

public sealed class DeviceCheckpoint : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public DateTime LastPulledAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? UserAccountId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long BucketKey { get; set; }
    public int ResponseStatusCode { get; set; }
    public string ResponseJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
