using System.Text.Json;
using Shopkeeper.Api.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace Shopkeeper.Api.Services;

public sealed class ApiCacheService(IFusionCache cache)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CachedApiResult<T>> GetOrSetAsync<T>(
        string key,
        TimeSpan duration,
        IEnumerable<string> tags,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        return await cache.GetOrSetAsync<CachedApiResult<T>>(
            key,
            async (_, token) =>
            {
                var value = await factory(token);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
                return new CachedApiResult<T>(value, ETagUtility.CreateWeak(bytes));
            },
            default,
            CreateOptions(duration),
            tags,
            ct);
    }

    public async Task InvalidateTagsAsync(IEnumerable<string> tags, CancellationToken ct)
    {
        var distinctTags = tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctTags.Length == 0)
        {
            return;
        }

        await cache.RemoveByTagAsync(distinctTags, null, ct);
    }

    private static FusionCacheEntryOptions CreateOptions(TimeSpan duration)
        => new(duration)
        {
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = duration + duration,
            FactorySoftTimeout = TimeSpan.FromSeconds(2),
            FactoryHardTimeout = TimeSpan.FromSeconds(10)
        };
}
