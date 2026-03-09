using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public enum IdempotencyBeginStatus
{
    Started,
    Completed,
    InProgress
}

public sealed record IdempotencyBeginResult(IdempotencyBeginStatus Status, IdempotencyRecord? Record, IResult? ExistingResult);

public sealed class IdempotencyService(ShopkeeperDbContext db)
{
    private static readonly long BucketWindowTicks = TimeSpan.FromMinutes(10).Ticks;

    public async Task<IdempotencyBeginResult> BeginAsync<TRequest>(
        Guid tenantId,
        string scope,
        HttpContext httpContext,
        TRequest request,
        string? clientRequestId,
        CancellationToken ct)
    {
        var key = BuildKey(scope, httpContext, request, clientRequestId);
        var bucketKey = SystemClock.Instance.GetCurrentInstant().ToUnixTimeTicks() / BucketWindowTicks;

        var existing = await db.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.Scope == scope &&
                x.IdempotencyKey == key &&
                x.BucketKey == bucketKey, ct);

        if (existing is not null)
        {
            if (existing.ResponseStatusCode > 0)
            {
                return new IdempotencyBeginResult(
                    IdempotencyBeginStatus.Completed,
                    existing,
                    Results.Content(existing.ResponseJson, contentType: "application/json", statusCode: existing.ResponseStatusCode));
            }

            return new IdempotencyBeginResult(
                IdempotencyBeginStatus.InProgress,
                existing,
                Results.Conflict(new { message = "An identical request is already processing." }));
        }

        var record = new IdempotencyRecord
        {
            TenantId = tenantId,
            Scope = scope,
            IdempotencyKey = key,
            BucketKey = bucketKey,
            ResponseStatusCode = 0,
            ResponseJson = string.Empty,
            CreatedAtUtc = SystemClock.Instance.GetCurrentInstant()
        };

        db.Set<IdempotencyRecord>().Add(record);

        try
        {
            await db.SaveChangesAsync(ct);
            return new IdempotencyBeginResult(IdempotencyBeginStatus.Started, record, null);
        }
        catch (DbUpdateException)
        {
            var raced = await db.Set<IdempotencyRecord>()
                .FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId &&
                    x.Scope == scope &&
                    x.IdempotencyKey == key &&
                    x.BucketKey == bucketKey, ct);

            if (raced is null)
            {
                throw;
            }

            if (raced.ResponseStatusCode > 0)
            {
                return new IdempotencyBeginResult(
                    IdempotencyBeginStatus.Completed,
                    raced,
                    Results.Content(raced.ResponseJson, contentType: "application/json", statusCode: raced.ResponseStatusCode));
            }

            return new IdempotencyBeginResult(
                IdempotencyBeginStatus.InProgress,
                raced,
                Results.Conflict(new { message = "An identical request is already processing." }));
        }
    }

    public async Task CompleteAsync(IdempotencyRecord record, int statusCode, object response, CancellationToken ct)
    {
        record.ResponseStatusCode = statusCode;
        record.ResponseJson = SyncJson.Serialize(response);
        await db.SaveChangesAsync(ct);
    }

    public async Task AbandonAsync(IdempotencyRecord? record, CancellationToken ct)
    {
        if (record is null)
        {
            return;
        }

        db.Set<IdempotencyRecord>().Remove(record);
        await db.SaveChangesAsync(ct);
    }

    private static string BuildKey<TRequest>(string scope, HttpContext httpContext, TRequest request, string? clientRequestId)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var deviceId = httpContext.Request.Headers["X-Device-Id"].FirstOrDefault() ?? "unknown-device";
        var material = string.IsNullOrWhiteSpace(clientRequestId)
            ? SyncJson.Serialize(request)
            : clientRequestId.Trim();
        var raw = $"{scope}|{userId}|{deviceId}|{material}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
