using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using NodaTime;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOrManager });

        group.MapPost("/items", CreateItem);
        group.MapGet("/items", ListItems);
        group.MapGet("/items/{id:guid}", GetItem);
        group.MapPatch("/items/{id:guid}", UpdateItem);
        group.MapDelete("/items/{id:guid}", DeleteItem);
        group.MapPost("/items/{id:guid}/photos", AddPhoto);
        group.MapPost("/stock-adjustments", CreateStockAdjustment);

        return app;
    }

    private static async Task<IResult> CreateItem(
        [FromBody] CreateInventoryItemRequest request,
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

        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            var serialExists = await db.InventoryItems.AnyAsync(x =>
                x.TenantId == tenantId.Value && x.SerialNumber == request.SerialNumber, ct);
            if (serialExists)
            {
                return Results.Conflict(new { message = "Serial number already exists in this shop." });
            }
        }

        var item = new InventoryItem
        {
            TenantId = tenantId.Value,
            ProductName = request.ProductName,
            ModelNumber = request.ModelNumber,
            SerialNumber = request.SerialNumber,
            Quantity = request.Quantity,
            ExpiryDate = request.ExpiryDate,
            CostPrice = request.CostPrice,
            SellingPrice = request.SellingPrice,
            ItemType = request.ItemType,
            ConditionGrade = request.ConditionGrade,
            ConditionNotes = request.ConditionNotes
        };

        db.InventoryItems.Add(item);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId.Value,
            UserAccountId = tenant.GetUserId(httpContext.User),
            Action = "inventory.create",
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            // Fixed: was using string interpolation which could corrupt JSON if ProductName
            // contained quotes or backslashes.
            PayloadJson = JsonSerializer.Serialize(new { productName = item.ProductName })
        });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);

        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = SyncConstants.ServerDeviceId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Operation = SyncOperation.Create,
            PayloadJson = SyncJson.Serialize(ToView(item)),
            ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await InvalidateInventoryCachesAsync(cache, tenantId.Value, ct);

        return Results.Created($"/api/v1/inventory/items/{item.Id}", ToView(item));
    }

    private static async Task<IResult> ListItems(
        [FromQuery] int? page,
        [FromQuery] int? limit,
        InventoryReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        var effectivePage = Math.Max(1, page.GetValueOrDefault(1));
        var effectiveLimit = Math.Clamp(limit.GetValueOrDefault(100), 1, 200);
        var cached = await reads.ListItemsAsync(tenantId.Value, effectivePage, effectiveLimit, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> GetItem(
        Guid id,
        InventoryReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        var cached = await reads.GetItemAsync(tenantId.Value, id, ct);
        return cached.Value is null
            ? Results.NotFound()
            : HttpCacheResults.OkOrNotModified(httpContext, cached!);
    }

    private static async Task<IResult> UpdateItem(
        Guid id,
        [FromBody] UpdateInventoryItemRequest request,
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

        var item = await db.InventoryItems.FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.RowVersionBase64))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "Precondition required", detail: "rowVersionBase64 is required for inventory updates.");
        }

        if (item.RowVersion.Length > 0 &&
            Convert.ToBase64String(item.RowVersion) != request.RowVersionBase64)
        {
            return Results.Conflict(new { message = "Conflict detected. Item has changed on server.", entityId = id });
        }

        if (!string.IsNullOrWhiteSpace(request.ProductName)) item.ProductName = request.ProductName;
        if (request.ModelNumber is not null) item.ModelNumber = request.ModelNumber;
        if (request.SerialNumber is not null) item.SerialNumber = request.SerialNumber;
        if (request.Quantity.HasValue) item.Quantity = request.Quantity.Value;
        if (request.CostPrice.HasValue) item.CostPrice = request.CostPrice.Value;
        if (request.SellingPrice.HasValue) item.SellingPrice = request.SellingPrice.Value;
        if (request.ExpiryDate.HasValue) item.ExpiryDate = request.ExpiryDate;
        if (request.ItemType.HasValue) item.ItemType = request.ItemType.Value;
        if (request.ConditionGrade.HasValue) item.ConditionGrade = request.ConditionGrade;
        if (request.ConditionNotes is not null) item.ConditionNotes = request.ConditionNotes;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await db.Entry(item).Collection(x => x.Photos).LoadAsync(ct);

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId.Value,
            UserAccountId = tenant.GetUserId(httpContext.User),
            Action = "inventory.update",
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            PayloadJson = SyncJson.Serialize(ToView(item))
        });
        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = SyncConstants.ServerDeviceId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Operation = SyncOperation.Update,
            PayloadJson = SyncJson.Serialize(ToView(item)),
            ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await InvalidateInventoryCachesAsync(cache, tenantId.Value, ct);
        return Results.Ok(ToView(item));
    }

    private static async Task<IResult> AddPhoto(
        Guid id,
        [FromBody] AddItemPhotoRequest request,
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

        var item = await db.InventoryItems
            .Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        var photo = new ItemPhoto
        {
            TenantId = tenantId.Value,
            InventoryItemId = id,
            PhotoUri = request.PhotoUri
        };

        // Fixed: single transaction wraps photo + audit + sync — no partial commits.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.ItemPhotos.Add(photo);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId.Value,
            UserAccountId = tenant.GetUserId(httpContext.User),
            Action = "inventory.photo.add",
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            PayloadJson = JsonSerializer.Serialize(new { photo.Id, photo.PhotoUri })
        });

        item.Photos.Add(photo); // add to in-memory collection so ToView reflects it
        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = SyncConstants.ServerDeviceId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Operation = SyncOperation.Update,
            PayloadJson = SyncJson.Serialize(ToView(item)),
            ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await InvalidateInventoryCachesAsync(cache, tenantId.Value, ct);

        return Results.Created($"/api/v1/inventory/items/{id}/photos/{photo.Id}", photo);
    }

    private static async Task<IResult> DeleteItem(
        Guid id,
        [FromQuery] string? rowVersionBase64,
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

        var item = await db.InventoryItems
            .Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);
        if (item is null || item.IsDeleted)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(rowVersionBase64))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "Precondition required", detail: "rowVersionBase64 is required for inventory deletes.");
        }

        if (item.RowVersion.Length > 0 &&
            Convert.ToBase64String(item.RowVersion) != rowVersionBase64)
        {
            return Results.Conflict(new { message = "Conflict detected. Item has changed on server.", entityId = id });
        }

        // Fixed: single transaction wrapping soft-delete + audit + sync — no partial commits.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        item.IsDeleted = true;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId.Value,
            UserAccountId = tenant.GetUserId(httpContext.User),
            Action = "inventory.delete",
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            PayloadJson = SyncJson.Serialize(new { item.Id, item.ProductName, item.IsDeleted })
        });
        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = SyncConstants.ServerDeviceId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Operation = SyncOperation.Delete,
            PayloadJson = SyncJson.Serialize(new { id = item.Id }),
            ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await InvalidateInventoryCachesAsync(cache, tenantId.Value, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> CreateStockAdjustment(
        [FromBody] StockAdjustmentRequest request,
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

        var item = await db.InventoryItems.FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == request.InventoryItemId, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.Quantity += request.DeltaQuantity;

        var adjustment = new StockAdjustment
        {
            TenantId = tenantId.Value,
            InventoryItemId = item.Id,
            DeltaQuantity = request.DeltaQuantity,
            Reason = request.Reason,
            CreatedByMembershipId = tenant.GetMembershipId(httpContext.User)
        };

        // Fixed: single transaction wrapping quantity update + audit + sync.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.StockAdjustments.Add(adjustment);
        await db.SaveChangesAsync(ct);

        await db.Entry(item).Collection(x => x.Photos).LoadAsync(ct);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId.Value,
            UserAccountId = tenant.GetUserId(httpContext.User),
            Action = "stock.adjustment.create",
            EntityName = nameof(StockAdjustment),
            EntityId = adjustment.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                adjustment.InventoryItemId,
                adjustment.DeltaQuantity,
                adjustment.Reason
            })
        });
        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = SyncConstants.ServerDeviceId,
            EntityName = nameof(InventoryItem),
            EntityId = item.Id,
            Operation = SyncOperation.Update,
            PayloadJson = SyncJson.Serialize(ToView(item)),
            ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await InvalidateInventoryCachesAsync(cache, tenantId.Value, ct);

        return Results.Ok(new { itemId = item.Id, item.Quantity, adjustmentId = adjustment.Id });
    }

    private static Task InvalidateInventoryCachesAsync(ApiCacheService cache, Guid tenantId, CancellationToken ct)
        => cache.InvalidateTagsAsync([ApiCacheTags.Inventory(tenantId), ApiCacheTags.Reports(tenantId)], ct);

    private static InventoryItemView ToView(InventoryItem item)
        => new(
            item.Id,
            item.ProductName,
            item.ModelNumber,
            item.SerialNumber,
            item.Quantity,
            item.ExpiryDate,
            item.CostPrice,
            item.SellingPrice,
            item.ItemType,
            item.ConditionGrade,
            item.ConditionNotes,
            item.Photos.Select(x => x.PhotoUri).ToList(),
            item.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(item.RowVersion));
}
