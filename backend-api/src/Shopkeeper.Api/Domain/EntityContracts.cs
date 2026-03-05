namespace Shopkeeper.Api.Domain;

public interface IMutableTenantEntity
{
    Guid TenantId { get; set; }
    DateTime UpdatedAtUtc { get; set; }
    byte[] RowVersion { get; set; }
}
