using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using NodaTime;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

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
    private const int MaxChangesPerPull = 500;

    private static async Task<IResult> PushChanges(
        [FromBody] SyncPushRequest request,
        ShopkeeperDbContext db,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        if (request.Changes.Count > MaxChangesPerPush)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["changes"] = [$"Maximum {MaxChangesPerPush} changes per push request."]
            });
        }

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
        }

        // Batch: load all referenced entities up-front to avoid N+1 per-change DB queries.
        var entityIds = request.Changes.Select(c => c.EntityId).Distinct().ToList();

        var inventoryItemIds = request.Changes
            .Where(c => c.EntityName == nameof(InventoryItem))
            .Select(c => c.EntityId)
            .Distinct()
            .ToList();
        var inventoryItems = inventoryItemIds.Count > 0
            ? await db.InventoryItems
                .Where(x => x.TenantId == tenantId.Value && inventoryItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct)
            : new Dictionary<Guid, InventoryItem>();

        var saleIds = request.Changes
            .Where(c => c.EntityName == nameof(Sale))
            .Select(c => c.EntityId)
            .Distinct()
            .ToList();
        var saleEntities = saleIds.Count > 0
            ? await db.Sales
                .Where(x => x.TenantId == tenantId.Value && saleIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct)
            : new Dictionary<Guid, Sale>();

        // Batch: pre-load already-accepted change fingerprints to avoid per-change AnyAsync.
        var alreadyAccepted = await db.SyncChanges
            .Where(x =>
                x.TenantId == tenantId.Value &&
                x.Status == SyncStatus.Accepted &&
                entityIds.Contains(x.EntityId))
            .Select(x => new { x.DeviceId, x.EntityId, x.ClientUpdatedAtUtc })
            .ToListAsync(ct);
        var alreadyAcceptedSet = alreadyAccepted
            .Select(x => (x.DeviceId, x.EntityId, x.ClientUpdatedAtUtc))
            .ToHashSet();

        var conflicts = new List<SyncConflictView>();
        var acceptedCount = 0;
        var cacheTagsToInvalidate = new HashSet<string>(StringComparer.Ordinal);

        foreach (var change in request.Changes)
        {
            if (alreadyAcceptedSet.Contains((change.DeviceId, change.EntityId, change.ClientUpdatedAtUtc)))
            {
                acceptedCount++;
                continue;
            }

            var conflict = DetectConflict(inventoryItems, saleEntities, change);
            if (conflict is null)
            {
                var applied = await ApplyAcceptedChange(db, tenantId.Value, inventoryItems, saleEntities, change, ct);
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
                CollectInvalidationTags(cacheTagsToInvalidate, tenantId.Value, change.EntityName);
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
        await cache.InvalidateTagsAsync(cacheTagsToInvalidate, ct);
        return Results.Ok(new SyncPushResponse(acceptedCount, conflicts));
    }

    private static async Task<IResult> PullChanges(
        [FromBody] SyncPullRequest request,
        ShopkeeperDbContext db,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        var nowUtc = SystemClock.Instance.GetCurrentInstant();
        var checkpoint = await db.DeviceCheckpoints
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.DeviceId == request.DeviceId, ct);

        var cursor = ParseCursor(request.Cursor);
        var baseSinceUtc = request.SinceUtc ?? checkpoint?.LastPulledAtUtc ?? nowUtc - Duration.FromDays(7);

        var query = db.SyncChanges
            .Where(x =>
                x.TenantId == tenantId.Value &&
                x.Status == SyncStatus.Accepted &&
                x.DeviceId != request.DeviceId);

        // Fixed: push the keyset predicate into SQL in both branches — no unbounded ToListAsync.
        IQueryable<SyncChangeCursorEnvelope> pagedQuery;
        if (cursor is null)
        {
            pagedQuery = query
                .Where(x => x.ServerUpdatedAtUtc >= baseSinceUtc && x.ServerUpdatedAtUtc < nowUtc)
                .OrderBy(x => x.ServerUpdatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => new SyncChangeCursorEnvelope(
                    x.Id,
                    x.ServerUpdatedAtUtc,
                    new SyncPushChange(
                        x.DeviceId,
                        x.EntityName,
                        x.EntityId,
                        x.Operation,
                        x.PayloadJson,
                        x.ClientUpdatedAtUtc,
                        null)));
        }
        else
        {
            // Keyset: rows strictly after cursor position, fully pushed to PostgreSQL.
            pagedQuery = query
                .Where(x =>
                    x.ServerUpdatedAtUtc > cursor.ServerUpdatedAtUtc ||
                    (x.ServerUpdatedAtUtc == cursor.ServerUpdatedAtUtc && x.Id.CompareTo(cursor.ChangeId) > 0))
                .OrderBy(x => x.ServerUpdatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => new SyncChangeCursorEnvelope(
                    x.Id,
                    x.ServerUpdatedAtUtc,
                    new SyncPushChange(
                        x.DeviceId,
                        x.EntityName,
                        x.EntityId,
                        x.Operation,
                        x.PayloadJson,
                        x.ClientUpdatedAtUtc,
                        null)));
        }

        var page = await pagedQuery
            .Take(MaxChangesPerPull + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > MaxChangesPerPull;
        if (hasMore) page = page.Take(MaxChangesPerPull).ToList();

        var last = page.LastOrDefault();
        var nextCursor = hasMore && last is not null ? EncodeCursor(last.ServerUpdatedAtUtc, last.Id) : null;
        var serverTimestampUtc = last?.ServerUpdatedAtUtc ?? nowUtc;

        if (checkpoint is null)
        {
            checkpoint = new DeviceCheckpoint
            {
                TenantId = tenantId.Value,
                DeviceId = request.DeviceId,
                LastPulledAtUtc = serverTimestampUtc
            };
            db.DeviceCheckpoints.Add(checkpoint);
        }
        else if (!hasMore)
        {
            checkpoint.LastPulledAtUtc = serverTimestampUtc;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new SyncPullResponse(serverTimestampUtc, page.Select(x => x.Change).ToList(), hasMore, nextCursor));
    }

    private static void CollectInvalidationTags(ISet<string> tags, Guid tenantId, string entityName)
    {
        if (entityName == nameof(InventoryItem))
        {
            tags.Add(ApiCacheTags.Inventory(tenantId));
            tags.Add(ApiCacheTags.Reports(tenantId));
            return;
        }

        if (entityName == nameof(Sale))
        {
            tags.Add(ApiCacheTags.Inventory(tenantId));
            tags.Add(ApiCacheTags.Sales(tenantId));
            tags.Add(ApiCacheTags.Credits(tenantId));
            tags.Add(ApiCacheTags.Reports(tenantId));
        }
    }

    // Fixed: uses pre-loaded dictionaries — no DB round-trip per change.
    private static ConflictInfo? DetectConflict(
        Dictionary<Guid, InventoryItem> inventoryItems,
        Dictionary<Guid, Sale> sales,
        SyncPushChange change)
    {
        if (change.EntityName == nameof(InventoryItem))
        {
            if (!inventoryItems.TryGetValue(change.EntityId, out var item))
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
            if (!sales.TryGetValue(change.EntityId, out var sale))
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

    private static async Task<AppliedSyncChange> ApplyAcceptedChange(
        ShopkeeperDbContext db,
        Guid tenantId,
        Dictionary<Guid, InventoryItem> inventoryItems,
        Dictionary<Guid, Sale> sales,
        SyncPushChange change,
        CancellationToken ct)
    {
        if (change.EntityName != nameof(InventoryItem))
        {
            return new AppliedSyncChange(change.Operation, change.PayloadJson);
        }

        return change.Operation switch
        {
            SyncOperation.Update => await ApplyInventoryUpdate(db, tenantId, inventoryItems, change, ct),
            SyncOperation.Delete => await ApplyInventoryDelete(db, tenantId, inventoryItems, change, ct),
            _ => new AppliedSyncChange(change.Operation, change.PayloadJson)
        };
    }

    private static async Task<AppliedSyncChange> ApplyInventoryUpdate(
        ShopkeeperDbContext db,
        Guid tenantId,
        Dictionary<Guid, InventoryItem> inventoryItems,
        SyncPushChange change,
        CancellationToken ct)
    {
        if (!inventoryItems.TryGetValue(change.EntityId, out var item) || item.IsDeleted)
        {
            throw new InvalidOperationException("Inventory item was not found for sync update.");
        }

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
        item.UpdatedAtUtc = SystemClock.Instance.GetCurrentInstant();

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            Action = "sync.inventory.update",
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            PayloadJson = SyncJson.Serialize(new { item.Id, item.ProductName, item.Quantity, source = "device-sync" })
        });

        // SaveChanges here so ApplyMutableEntityUpdates sets the new RowVersion before we
        // capture it in the snapshot payload sent back to other devices.
        await db.SaveChangesAsync(ct);
        return new AppliedSyncChange(SyncOperation.Update, BuildInventorySnapshotPayload(item));
    }

    private static async Task<AppliedSyncChange> ApplyInventoryDelete(
        ShopkeeperDbContext db,
        Guid tenantId,
        Dictionary<Guid, InventoryItem> inventoryItems,
        SyncPushChange change,
        CancellationToken ct)
    {
        if (!inventoryItems.TryGetValue(change.EntityId, out var item))
        {
            return new AppliedSyncChange(SyncOperation.Delete, "{}");
        }

        item.IsDeleted = true;
        item.UpdatedAtUtc = SystemClock.Instance.GetCurrentInstant();

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
            expiryDate = item.ExpiryDate?.ToString("uuuu-MM-dd", null),
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
            updatedAtUtc = sale.UpdatedAtUtc,
            rowVersionBase64 = sale.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(sale.RowVersion)
        });
    }

    private static bool IsAllowedForRole(MembershipRole role, SyncPushChange change)
    {
        return change.EntityName switch
        {
            nameof(InventoryItem) => RoleCapabilities.CanManageInventory(role),
            nameof(Sale) => RoleCapabilities.CanManageSales(role),
            _ => true
        };
    }

    private static string EncodeCursor(Instant updatedAtUtc, Guid changeId)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{updatedAtUtc.ToUnixTimeTicks()}|{changeId}"));
    }

    private static SyncCursor? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return null;
            }

            return long.TryParse(parts[0], out var ticks) && Guid.TryParse(parts[1], out var changeId)
                ? new SyncCursor(Instant.FromUnixTimeTicks(ticks), changeId)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ConflictInfo(string Reason, string? ServerPayloadJson, string? ServerRowVersionBase64);
    private sealed record AppliedSyncChange(SyncOperation Operation, string PayloadJson);
    private sealed record SyncChangeCursorEnvelope(Guid Id, Instant ServerUpdatedAtUtc, SyncPushChange Change);
    private sealed record SyncCursor(Instant ServerUpdatedAtUtc, Guid ChangeId);
}
