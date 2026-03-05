using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sync")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.StaffOrOwner });

        group.MapPost("/push", PushChanges);
        group.MapPost("/pull", PullChanges);

        return app;
    }

    private static async Task<IResult> PushChanges(
        [FromBody] SyncPushRequest request,
        ShopkeeperDbContext db,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        var conflicts = new List<SyncConflictView>();
        var acceptedCount = 0;

        foreach (var change in request.Changes)
        {
            var conflict = await DetectConflict(db, tenantId.Value, change, ct);
            var status = conflict is null ? SyncStatus.Accepted : SyncStatus.Conflict;

            var entity = new SyncChange
            {
                TenantId = tenantId.Value,
                DeviceId = change.DeviceId,
                EntityName = change.EntityName,
                EntityId = change.EntityId,
                Operation = change.Operation,
                PayloadJson = change.PayloadJson,
                ClientUpdatedAtUtc = change.ClientUpdatedAtUtc,
                Status = status,
                ConflictReason = conflict?.Reason
            };

            db.SyncChanges.Add(entity);

            if (conflict is null)
            {
                acceptedCount++;
            }
            else
            {
                conflicts.Add(new SyncConflictView(
                    entity.Id,
                    change.EntityName,
                    change.EntityId,
                    conflict.Reason,
                    conflict.ServerPayloadJson,
                    conflict.ServerRowVersionBase64));
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new SyncPushResponse(acceptedCount, conflicts));
    }

    private static async Task<IResult> PullChanges(
        [FromBody] SyncPullRequest request,
        ShopkeeperDbContext db,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        var sinceUtc = request.SinceUtc ?? DateTime.UtcNow.AddDays(-7);

        var changes = await db.SyncChanges
            .Where(x =>
                x.TenantId == tenantId.Value &&
                x.Status == SyncStatus.Accepted &&
                x.ServerUpdatedAtUtc >= sinceUtc &&
                x.DeviceId != request.DeviceId)
            .OrderBy(x => x.ServerUpdatedAtUtc)
            .Select(x => new SyncPushChange(
                x.DeviceId,
                x.EntityName,
                x.EntityId,
                x.Operation,
                x.PayloadJson,
                x.ClientUpdatedAtUtc,
                null))
            .ToListAsync(ct);

        var checkpoint = await db.DeviceCheckpoints
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.DeviceId == request.DeviceId, ct);

        if (checkpoint is null)
        {
            checkpoint = new DeviceCheckpoint
            {
                TenantId = tenantId.Value,
                DeviceId = request.DeviceId,
                LastPulledAtUtc = DateTime.UtcNow
            };
            db.DeviceCheckpoints.Add(checkpoint);
        }
        else
        {
            checkpoint.LastPulledAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new SyncPullResponse(DateTime.UtcNow, changes));
    }

    private static async Task<ConflictInfo?> DetectConflict(ShopkeeperDbContext db, Guid tenantId, SyncPushChange change, CancellationToken ct)
    {
        if (change.Operation != SyncOperation.Update || string.IsNullOrWhiteSpace(change.RowVersionBase64))
        {
            return null;
        }

        if (change.EntityName == nameof(InventoryItem))
        {
            var item = await db.InventoryItems.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == change.EntityId, ct);
            if (item is null)
            {
                return null;
            }

            var serverVersion = item.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(item.RowVersion);
            if (!string.Equals(serverVersion, change.RowVersionBase64, StringComparison.Ordinal))
            {
                return new ConflictInfo(
                    "Inventory item row version mismatch.",
                    BuildInventorySnapshotPayload(item),
                    serverVersion);
            }
        }

        if (change.EntityName == nameof(Sale))
        {
            var sale = await db.Sales.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == change.EntityId, ct);
            if (sale is null)
            {
                return null;
            }

            var serverVersion = sale.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(sale.RowVersion);
            if (!string.Equals(serverVersion, change.RowVersionBase64, StringComparison.Ordinal))
            {
                return new ConflictInfo(
                    "Sale row version mismatch.",
                    BuildSaleSnapshotPayload(sale),
                    serverVersion);
            }
        }

        return null;
    }

    private static string BuildInventorySnapshotPayload(InventoryItem item)
    {
        return JsonSerializer.Serialize(new
        {
            item.Id,
            item.ProductName,
            item.ModelNumber,
            item.SerialNumber,
            item.Quantity,
            ExpiryDate = item.ExpiryDate?.ToString("yyyy-MM-dd"),
            item.CostPrice,
            item.SellingPrice,
            ItemType = (int)item.ItemType,
            ConditionGrade = item.ConditionGrade.HasValue ? (int)item.ConditionGrade.Value : (int?)null,
            item.ConditionNotes,
            item.UpdatedAtUtc
        });
    }

    private static string BuildSaleSnapshotPayload(Sale sale)
    {
        return JsonSerializer.Serialize(new
        {
            sale.Id,
            sale.SaleNumber,
            sale.Subtotal,
            sale.VatAmount,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.OutstandingAmount,
            sale.IsCredit,
            sale.DueDateUtc,
            Status = sale.Status.ToString(),
            sale.IsVoided,
            sale.UpdatedAtUtc
        });
    }

    private sealed record ConflictInfo(string Reason, string ServerPayloadJson, string ServerRowVersionBase64);
}
