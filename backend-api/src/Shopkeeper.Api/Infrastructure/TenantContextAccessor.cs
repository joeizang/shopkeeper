using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Shopkeeper.Api.Infrastructure;

public sealed class TenantContextAccessor
{
    public Guid? GetTenantId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(CustomClaimTypes.TenantId);
        return Guid.TryParse(value, out var tenantId) ? tenantId : null;
    }

    public Guid? GetUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public Guid? GetMembershipId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(CustomClaimTypes.MembershipId);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
