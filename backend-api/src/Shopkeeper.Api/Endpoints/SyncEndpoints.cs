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
            .RequireAuthorization()
            .RequireRateLimiting("sync");

        group.MapPost("/push", PushChanges);
        group.MapPost("/pull", PullChanges);

        return app;
    }

    private const int MaxChangesPerPush = 200;

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

        // Prevent oversized batches — each change runs multiple DB queries.
        if (request.Changes.Count > MaxChangesPerPush)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["changes"] = [$"Maximum {MaxChangesPerPush} changes per push request."]
            });
        }

        var conflicts = new List<SyncConflictView>();
        var acceptedCount = 0;
        var role = RoleCapabilities.GetRole(httpContext.User);

        if (!role.HasValue)
        {
            return Results.Forbid();
        }

        foreach (var change in request.Changes)
        {
            if (!IsAllowedForRole(role.Value, change))
            {
                return Results.Forbid();
            }

            // Idempotency: if this exact change was already accepted, count it and skip.
            var alreadyApplied = await db.SyncChanges.AnyAsync(x =>
                x.TenantId == tenantId.Value &&
                x.DeviceId == change.DeviceId &&
                x.EntityId == change.EntityId &&
                x.ClientUpdatedAtUtc == change.ClientUpdatedAtUtc &&
                x.Status == SyncStatus.Accepted, ct);

            if (alreadyApplied)
            {
                acceptedCount++;
                continue;
            }

            var conflict = await DetectConflict(db, tenantId.Value, change, ct);
            if (conflict is null)
            {
                var applied = await ApplyAcceptedChange(db, tenantId.Value, change, ct);
                db.SyncChanges.Add(new SyncChange
                {
                    TenantId = tenantId.Value,
                    DeviceId = change.DeviceId,
                    EntityName = change.EntityName,
                    EntityId = change.EntityId,
                    Operation = applied.Operation,
                    PayloadJson = applied.PayloadJson,
                    ClientUpdatedAtUtc = change.ClientUpdatedAtUtc,
                    Status = SyncStatus.Accepted
                });
                acceptedCount++;
            }
            else
            {
                var entity = new SyncChange
                {
                    TenantId = tenantId.Value,
                    DeviceId = change.DeviceId,
                    EntityName = change.EntityName,
                    EntityId = change.EntityId,
                    Operation = change.Operation,
                    PayloadJson = change.PayloadJson,
                    ClientUpdatedAtUtc = change.ClientUpdatedAtUtc,
                    Status = SyncStatus.Conflict,
                    ConflictReason = conflict.Reason
                };

                db.SyncChanges.Add(entity);
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

        var nowUtc = DateTime.UtcNow;
        var sinceUtc = request.SinceUtc ?? nowUtc.AddDays(-7);

        var changes = await db.SyncChanges
            .Where(x =>
                x.TenantId == tenantId.Value &&
                x.Status == SyncStatus.Accepted &&
                x.ServerUpdatedAtUtc >= sinceUtc &&
                x.ServerUpdatedAtUtc < nowUtc &&
                x.DeviceId != request.DeviceId)
            .OrderBy(x => x.ServerUpdatedAtUtc)
            .Take(500)
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
                LastPulledAtUtc = nowUtc
            };
            db.DeviceCheckpoints.Add(checkpoint);
        }
        else
        {
            checkpoint.LastPulledAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new SyncPullResponse(nowUtc, changes));
    }

    private static async Task<ConflictInfo?> DetectConflict(ShopkeeperDbContext db, Guid tenantId, SyncPushChange change, CancellationToken ct)
    {
        if (change.EntityName == nameof(InventoryItem))
        {
            var item = await db.InventoryItems.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == change.EntityId, ct);
            if (item is null)
            {
                return change.Operation is SyncOperation.Update or SyncOperation.Delete
                    ? new ConflictInfo("Inventory item no longer exists on server.", null, null)
                    : null;
            }

            if (change.Operation != SyncOperation.Update && change.Operation != SyncOperation.Delete)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(change.RowVersionBase64))
            {
                return new ConflictInfo(
                    "Inventory item update is missing a row version.",
                    BuildInventorySnapshotPayload(item),
                    item.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(item.RowVersion));
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
                return change.Operation is SyncOperation.Update or SyncOperation.Delete
                    ? new ConflictInfo("Sale no longer exists on server.", null, null)
                    : null;
            }

            if (change.Operation != SyncOperation.Update && change.Operation != SyncOperation.Delete)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(change.RowVersionBase64))
            {
                return new ConflictInfo(
                    "Sale update is missing a row version.",
                    BuildSaleSnapshotPayload(sale),
                    sale.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(sale.RowVersion));
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

    private static async Task<AppliedSyncChange> ApplyAcceptedChange(ShopkeeperDbContext db, Guid tenantId, SyncPushChange change, CancellationToken ct)
    {
        if (change.EntityName != nameof(InventoryItem))
        {
            return new AppliedSyncChange(change.Operation, change.PayloadJson);
        }

        return change.Operation switch
        {
            SyncOperation.Update => await ApplyInventoryUpdate(db, tenantId, change, ct),
            SyncOperation.Delete => await ApplyInventoryDelete(db, tenantId, change, ct),
            _ => new AppliedSyncChange(change.Operation, change.PayloadJson)
        };
    }

    private static async Task<AppliedSyncChange> ApplyInventoryUpdate(ShopkeeperDbContext db, Guid tenantId, SyncPushChange change, CancellationToken ct)
    {
        var item = await db.InventoryItems
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == change.EntityId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("Inventory item was not found for sync update.");

        var payload = SyncJson.Deserialize<CreateInventoryItemRequest>(change.PayloadJson)
            ?? throw new InvalidOperationException("Inventory sync payload could not be parsed.");

        item.ProductName = payload.ProductName;
        item.ModelNumber = payload.ModelNumber;
        item.SerialNumber = payload.SerialNumber;
        item.Quantity = payload.Quantity;
        item.ExpiryDate = payload.ExpiryDate;
        item.CostPrice = payload.CostPrice;
        item.SellingPrice = payload.SellingPrice;
        item.ItemType = payload.ItemType;
        item.ConditionGrade = payload.ConditionGrade;
        item.ConditionNotes = payload.ConditionNotes;
        item.UpdatedAtUtc = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            Action = "sync.inventory.update",
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            PayloadJson = SyncJson.Serialize(new { item.Id, item.ProductName, item.Quantity, source = "device-sync" })
        });

        await db.SaveChangesAsync(ct);
        return new AppliedSyncChange(SyncOperation.Update, BuildInventorySnapshotPayload(item));
    }

    private static async Task<AppliedSyncChange> ApplyInventoryDelete(ShopkeeperDbContext db, Guid tenantId, SyncPushChange change, CancellationToken ct)
    {
        var item = await db.InventoryItems
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == change.EntityId, ct);

        if (item is null)
        {
            return new AppliedSyncChange(SyncOperation.Delete, "{}");
        }

        item.IsDeleted = true;
        item.UpdatedAtUtc = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            Action = "sync.inventory.delete",
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            PayloadJson = SyncJson.Serialize(new { item.Id, source = "device-sync" })
        });

        await db.SaveChangesAsync(ct);
        return new AppliedSyncChange(SyncOperation.Delete, "{}");
    }

    private static string BuildInventorySnapshotPayload(InventoryItem item)
    {
        return SyncJson.Serialize(new
        {
            id = item.Id,
            productName = item.ProductName,
            modelNumber = item.ModelNumber,
            serialNumber = item.SerialNumber,
            quantity = item.Quantity,
            expiryDate = item.ExpiryDate?.ToString("yyyy-MM-dd"),
            costPrice = item.CostPrice,
            sellingPrice = item.SellingPrice,
            itemType = (int)item.ItemType,
            conditionGrade = item.ConditionGrade.HasValue ? (int)item.ConditionGrade.Value : (int?)null,
            conditionNotes = item.ConditionNotes,
            photoUris = Array.Empty<string>(),
            rowVersionBase64 = item.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(item.RowVersion)
        });
    }

    private static string BuildSaleSnapshotPayload(Sale sale)
    {
        return SyncJson.Serialize(new
        {
            id = sale.Id,
            saleNumber = sale.SaleNumber,
            subtotal = sale.Subtotal,
            vatAmount = sale.VatAmount,
            discountAmount = sale.DiscountAmount,
            totalAmount = sale.TotalAmount,
            outstandingAmount = sale.OutstandingAmount,
            status = sale.Status.ToString(),
            isCredit = sale.IsCredit,
            dueDateUtc = sale.DueDateUtc,
            updatedAtUtc = sale.UpdatedAtUtc
        });
    }

    private sealed record ConflictInfo(string Reason, string? ServerPayloadJson, string? ServerRowVersionBase64);
    private sealed record AppliedSyncChange(SyncOperation Operation, string PayloadJson);

    private static bool IsAllowedForRole(MembershipRole role, SyncPushChange change)
    {
        return change.EntityName switch
        {
            nameof(InventoryItem) => RoleCapabilities.CanManageInventory(role),
            nameof(Sale) => RoleCapabilities.CanManageSales(role),
            _ => false
        };
    }
}
