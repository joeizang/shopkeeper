using Microsoft.Extensions.Primitives;

namespace Shopkeeper.Api.Infrastructure;

public sealed record CachedApiResult<T>(T Value, string ETag);

public static class HttpCacheResults
{
    public static IResult OkOrNotModified<T>(HttpContext httpContext, CachedApiResult<T> cached)
    {
        ApplyPrivateRevalidationHeaders(httpContext.Response, cached.ETag);
        return MatchesIfNoneMatch(httpContext.Request, cached.ETag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Ok(cached.Value);
    }

    public static IResult FileOrNotModified(
        HttpContext httpContext,
        byte[] content,
        string contentType,
        string fileName,
        string etag)
    {
        ApplyImmutableFileHeaders(httpContext.Response, etag);
        return MatchesIfNoneMatch(httpContext.Request, etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.File(content, contentType, fileName);
    }

    public static void ApplyPrivateRevalidationHeaders(HttpResponse response, string etag)
    {
        response.Headers.ETag = etag;
        response.Headers.CacheControl = "private, no-cache";
        AppendVaryAuthorization(response.Headers);
    }

    public static void ApplyImmutableFileHeaders(HttpResponse response, string etag)
    {
        response.Headers.ETag = etag;
        response.Headers.CacheControl = "private, max-age=86400, immutable";
        AppendVaryAuthorization(response.Headers);
    }

    public static bool MatchesIfNoneMatch(HttpRequest request, string currentEtag)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var values))
        {
            return false;
        }

        foreach (var raw in values.SelectMany(SplitValues))
        {
            var candidate = raw.Trim();
            if (candidate == "*" || string.Equals(candidate, currentEtag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendVaryAuthorization(IHeaderDictionary headers)
    {
        var current = headers.Vary;
        if (StringValues.IsNullOrEmpty(current))
        {
            headers.Vary = "Authorization";
            return;
        }

        if (current.ToString().Contains("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        headers.Vary = $"{current}, Authorization";
    }

    private static IEnumerable<string> SplitValues(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
