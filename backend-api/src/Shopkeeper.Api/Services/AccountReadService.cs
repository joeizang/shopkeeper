using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class AccountReadService(ShopkeeperDbContext db, ApiCacheService cache)
{
    private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(2);

    public Task<CachedApiResult<AccountProfileResponse?>> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        return cache.GetOrSetAsync(
            ApiCacheKeys.AccountProfile(userId),
            ProfileTtl,
            [ApiCacheTags.UserAccount(userId)],
            async token =>
            {
                var user = await db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == userId, token);

                return user is null
                    ? null
                    : new AccountProfileResponse(
                        user.Id,
                        user.FullName,
                        user.Email,
                        user.PhoneNumber,
                        user.AvatarUrl,
                        user.PreferredLanguage,
                        user.Timezone,
                        user.CreatedAtUtc);
            },
            ct);
    }
}
