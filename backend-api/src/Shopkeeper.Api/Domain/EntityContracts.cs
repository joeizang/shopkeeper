using NodaTime;

namespace Shopkeeper.Api.Domain;

public interface IMutableTenantEntity
{
    Guid TenantId { get; set; }
    Instant UpdatedAtUtc { get; set; }
    byte[] RowVersion { get; set; }
}
