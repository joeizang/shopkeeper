using NodaTime;

namespace Shopkeeper.Api.Domain;

public sealed class Expense : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public decimal Amount { get; set; }
    public Instant ExpenseDateUtc { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedByUserAccountId { get; set; }
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant UpdatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class ReportJob : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Status { get; set; } = "Completed";
    public string? FilterJson { get; set; }
    public string? FailureReason { get; set; }
    public Guid RequestedByUserAccountId { get; set; }
    public Guid? ReportFileId { get; set; }
    public Instant RequestedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant? CompletedAtUtc { get; set; }
    public Instant UpdatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ReportFile? ReportFile { get; set; }
}

public sealed class ReportFile : IMutableTenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long ByteLength { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public Guid CreatedByUserAccountId { get; set; }
    public Instant CreatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant UpdatedAtUtc { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
