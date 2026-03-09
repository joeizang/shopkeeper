using NodaTime;

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
    public Instant ClientUpdatedAtUtc { get; set; }
    public Instant ServerUpdatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public SyncStatus Status { get; set; } = SyncStatus.Accepted;
    public string? ConflictReason { get; set; }
}

public sealed class DeviceCheckpoint : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public Instant LastPulledAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant UpdatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
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
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
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
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
