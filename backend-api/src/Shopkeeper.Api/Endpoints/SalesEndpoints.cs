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

public static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sales")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.SalesAccess });

        group.MapPost("/", CreateSale);
        group.MapGet("/{id:guid}", GetSale);
        group.MapPost("/{id:guid}/payments", AddPayment);
        group.MapPost("/{id:guid}/void", VoidSale)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOrManager });
        group.MapGet("/{id:guid}/receipt", GetReceipt);

        return app;
    }

    private static async Task<IResult> CreateSale(
        [FromBody] CreateSaleRequest request,
        ShopkeeperDbContext db,
        SaleCalculator calculator,
        IdempotencyService idempotency,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var validation = ValidateCreateSale(request);
        if (validation.Count > 0)
        {
            return Results.ValidationProblem(validation);
        }

        var tenantId = tenant.GetTenantId(httpContext.User);
        var membershipId = tenant.GetMembershipId(httpContext.User);
        if (!tenantId.HasValue || !membershipId.HasValue)
        {
            return Results.Unauthorized();
        }

        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId.Value, ct);
        if (shop is null)
        {
            return Results.NotFound(new { message = "Shop not found." });
        }

        var items = await db.InventoryItems
            .Where(x => x.TenantId == tenantId.Value && request.Lines.Select(l => l.InventoryItemId).Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var line in request.Lines)
        {
            if (!items.TryGetValue(line.InventoryItemId, out var inventoryItem))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["inventoryItemId"] = [$"Item {line.InventoryItemId} not found"] });
            }

            if (inventoryItem.Quantity < line.Quantity)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["quantity"] = [$"Insufficient stock for {inventoryItem.ProductName}"] });
            }
        }

        var totals = calculator.Calculate(request.Lines, request.DiscountAmount, shop.VatEnabled, shop.VatRate);
        if (request.DiscountAmount > totals.Subtotal)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["discountAmount"] = ["Discount amount cannot exceed the sale subtotal."] });
        }

        var paidAmount = request.InitialPayments?.Sum(p => p.Amount) ?? 0m;
        if (paidAmount > totals.TotalAmount)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["initialPayments"] = ["Initial payments cannot exceed the sale total."] });
        }

        var idem = await idempotency.BeginAsync(tenantId.Value, "sales.create", httpContext, request, request.ClientRequestId, ct);
        if (idem.Status != IdempotencyBeginStatus.Started)
        {
            return idem.ExistingResult!;
        }

        try
        {
            var outstanding = totals.TotalAmount - paidAmount;
            var sale = new Sale
            {
                TenantId = tenantId.Value,
                SaleNumber = string.Empty, // set atomically inside the transaction below
                CustomerName = request.CustomerName?.Trim(),
                CustomerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
                DiscountAmount = totals.DiscountAmount,
                Subtotal = totals.Subtotal,
                VatAmount = totals.VatAmount,
                TotalAmount = totals.TotalAmount,
                OutstandingAmount = outstanding,
                IsCredit = request.IsCredit || outstanding > 0,
                DueDateUtc = request.DueDateUtc,
                Status = outstanding > 0 ? SaleStatus.PartiallyPaid : SaleStatus.Completed,
                CreatedByMembershipId = membershipId.Value
            };

            foreach (var line in request.Lines)
            {
                var item = items[line.InventoryItemId];
                item.Quantity -= line.Quantity;

                sale.Lines.Add(new SaleLine
                {
                    TenantId = tenantId.Value,
                    InventoryItemId = line.InventoryItemId,
                    ProductNameSnapshot = item.ProductName,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    CostPriceSnapshot = item.CostPrice,
                    LineTotal = line.UnitPrice * line.Quantity
                });
            }

            if (request.InitialPayments is not null)
            {
                foreach (var payment in request.InitialPayments)
                {
                    sale.Payments.Add(new SalePayment
                    {
                        TenantId = tenantId.Value,
                        Method = payment.Method,
                        Amount = payment.Amount,
                        Reference = payment.Reference
                    });
                }
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Atomically upsert the per-tenant sale counter and retrieve the next sequence
            // number. PostgreSQL's ON CONFLICT DO UPDATE is atomic — no race condition.
            var seqRows = await db.Database.SqlQuery<int>($"""
                INSERT INTO "TenantSaleCounters" ("TenantId", "NextSaleNumber")
                VALUES ({tenantId.Value}, 1)
                ON CONFLICT ("TenantId") DO UPDATE
                    SET "NextSaleNumber" = "TenantSaleCounters"."NextSaleNumber" + 1
                RETURNING "NextSaleNumber"
                """).ToListAsync(ct);
            var saleSeq = seqRows.Count > 0 ? seqRows[0] : 1;
            sale.SaleNumber = $"SL-{SystemClock.Instance.GetCurrentInstant().ToDateTimeUtc():yyyyMMdd}-{saleSeq:0000}";

            db.Sales.Add(sale);

            if (sale.IsCredit && outstanding > 0)
            {
                db.CreditAccounts.Add(new CreditAccount
                {
                    TenantId = tenantId.Value,
                    Sale = sale,
                    DueDateUtc = request.DueDateUtc ?? SystemClock.Instance.GetCurrentInstant() + Duration.FromDays(30),
                    OutstandingAmount = outstanding,
                    Status = CreditStatus.Open
                });
            }

            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId.Value,
                UserAccountId = tenant.GetUserId(httpContext.User),
                Action = "sales.create",
                EntityName = nameof(Sale),
                EntityId = sale.Id,
                PayloadJson = JsonSerializer.Serialize(new { sale.SaleNumber, sale.TotalAmount })
            });
            db.SyncChanges.Add(new SyncChange
            {
                TenantId = tenantId.Value,
                DeviceId = SyncConstants.ServerDeviceId,
                EntityName = nameof(Sale),
                EntityId = sale.Id,
                Operation = SyncOperation.Create,
                PayloadJson = SyncJson.Serialize(BuildSaleSyncPayload(sale)),
                ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await InvalidateSaleRelatedCachesAsync(cache, tenantId.Value, includeInventory: true, ct);

            var response = new { sale.Id, sale.SaleNumber, sale.TotalAmount, sale.OutstandingAmount };
            await idempotency.CompleteAsync(idem.Record!, StatusCodes.Status201Created, response, ct);
            return Results.Created($"/api/v1/sales/{sale.Id}", response);
        }
        catch
        {
            await idempotency.AbandonAsync(idem.Record, ct);
            throw;
        }
    }

    private static async Task<IResult> GetSale(
        Guid id,
        SaleReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        var cached = await reads.GetSaleAsync(tenantId.Value, id, ct);
        return cached.Value is null
            ? Results.NotFound()
            : HttpCacheResults.OkOrNotModified(httpContext, cached!);
    }

    private static async Task<IResult> AddPayment(
        Guid id,
        [FromBody] AddSalePaymentRequest request,
        ShopkeeperDbContext db,
        CreditLedgerService creditService,
        IdempotencyService idempotency,
        ApiCacheService cache,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var validation = ValidatePaymentRequest(request.Method, request.Amount, request.Reference, "amount");
        if (validation.Count > 0)
        {
            return Results.ValidationProblem(validation);
        }

        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue)
        {
            return Results.Unauthorized();
        }

        var sale = await db.Sales
            .Include(x => x.CreditAccount)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);

        if (sale is null)
        {
            return Results.NotFound();
        }

        if (sale.IsVoided)
        {
            return Results.Conflict(new { message = "Voided sales cannot receive new payments." });
        }

        if (request.Amount > sale.OutstandingAmount)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Payment amount cannot exceed the outstanding balance."] });
        }

        var idem = await idempotency.BeginAsync(tenantId.Value, $"sales.payment:{id}", httpContext, request, request.ClientRequestId, ct);
        if (idem.Status != IdempotencyBeginStatus.Started)
        {
            return idem.ExistingResult!;
        }

        try
        {
            var payment = new SalePayment
            {
                TenantId = tenantId.Value,
                SaleId = sale.Id,
                Method = request.Method,
                Amount = request.Amount,
                Reference = request.Reference?.Trim()
            };

            db.SalePayments.Add(payment);

            sale.OutstandingAmount -= request.Amount;
            sale.Status = sale.OutstandingAmount == 0 ? SaleStatus.Completed : SaleStatus.PartiallyPaid;

            if (sale.CreditAccount is not null)
            {
                creditService.ApplyRepayment(sale.CreditAccount, request.Amount);
                db.CreditRepayments.Add(new CreditRepayment
                {
                    TenantId = tenantId.Value,
                    CreditAccountId = sale.CreditAccount.Id,
                    SalePayment = payment,
                    Amount = request.Amount,
                    Notes = request.Note
                });
            }

            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId.Value,
                UserAccountId = tenant.GetUserId(httpContext.User),
                Action = "sales.payment.add",
                EntityName = nameof(SalePayment),
                EntityId = payment.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    payment.SaleId,
                    payment.Method,
                    payment.Amount,
                    payment.Reference
                })
            });
            db.SyncChanges.Add(new SyncChange
            {
                TenantId = tenantId.Value,
                DeviceId = SyncConstants.ServerDeviceId,
                EntityName = nameof(Sale),
                EntityId = sale.Id,
                Operation = SyncOperation.Update,
                PayloadJson = SyncJson.Serialize(BuildSaleSyncPayload(sale)),
                ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
            });

            await db.SaveChangesAsync(ct);
            await InvalidateSaleRelatedCachesAsync(cache, tenantId.Value, includeInventory: false, ct);

            var response = new
            {
                saleId = sale.Id,
                paymentId = payment.Id,
                sale.OutstandingAmount,
                status = sale.Status.ToString()
            };
            await idempotency.CompleteAsync(idem.Record!, StatusCodes.Status200OK, response, ct);
            return Results.Ok(response);
        }
        catch
        {
            await idempotency.AbandonAsync(idem.Record, ct);
            throw;
        }
    }

    private static async Task<IResult> VoidSale(
        Guid id,
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

        var sale = await db.Sales
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);

        if (sale is null)
        {
            return Results.NotFound();
        }

        if (sale.IsVoided)
        {
            return Results.Conflict(new { message = "Sale already voided." });
        }

        var itemIds = sale.Lines.Select(x => x.InventoryItemId).ToList();
        var items = await db.InventoryItems
            .Where(x => x.TenantId == tenantId.Value && itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var line in sale.Lines)
        {
            if (items.TryGetValue(line.InventoryItemId, out var item))
            {
                item.Quantity += line.Quantity;
            }
        }

        sale.IsVoided = true;
        sale.Status = SaleStatus.Void;
        sale.OutstandingAmount = 0;

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId.Value,
            UserAccountId = tenant.GetUserId(httpContext.User),
            Action = "sales.void",
            EntityName = nameof(Sale),
            EntityId = sale.Id,
            PayloadJson = JsonSerializer.Serialize(new { sale.SaleNumber, sale.Id })
        });
        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = SyncConstants.ServerDeviceId,
            EntityName = nameof(Sale),
            EntityId = sale.Id,
            Operation = SyncOperation.Update,
            PayloadJson = SyncJson.Serialize(BuildSaleSyncPayload(sale)),
            ClientUpdatedAtUtc = SystemClock.Instance.GetCurrentInstant()
        });

        await db.SaveChangesAsync(ct);
        await InvalidateSaleRelatedCachesAsync(cache, tenantId.Value, includeInventory: true, ct);

        return Results.Ok(new { saleId = sale.Id, status = "void" });
    }

    private static async Task<IResult> GetReceipt(
        Guid id,
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

        var sale = await db.Sales
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);

        if (sale is null)
        {
            return Results.NotFound();
        }

        var shop = await db.Shops.AsNoTracking().FirstAsync(x => x.Id == tenantId.Value, ct);

        var response = new ReceiptView(
            sale.Id,
            sale.SaleNumber,
            sale.CreatedAtUtc,
            shop.Name,
            sale.CustomerName,
            sale.Subtotal,
            sale.VatAmount,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.Payments.Sum(x => x.Amount),
            sale.OutstandingAmount,
            sale.Lines.Select(x => new ReceiptLineView(x.ProductNameSnapshot, x.Quantity, x.UnitPrice, x.LineTotal)).ToList(),
            sale.Payments.Select(x => new SalePaymentRequest(x.Method, x.Amount, x.Reference)).ToList());

        return Results.Ok(response);
    }

    private static Dictionary<string, string[]> ValidateCreateSale(CreateSaleRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Lines.Count == 0)
        {
            errors["lines"] = ["At least one line is required."];
        }
        else if (request.Lines.Any(x => x.Quantity <= 0))
        {
            errors["quantity"] = ["Every sale line quantity must be greater than zero."];
        }
        else if (request.Lines.Any(x => x.UnitPrice <= 0))
        {
            errors["unitPrice"] = ["Every sale line unit price must be greater than zero."];
        }

        if (request.DiscountAmount < 0)
        {
            errors["discountAmount"] = ["Discount amount cannot be negative."];
        }

        if (request.DueDateUtc.HasValue && request.DueDateUtc.Value < SystemClock.Instance.GetCurrentInstant())
        {
            errors["dueDateUtc"] = ["Due date cannot be in the past."];
        }

        if (request.InitialPayments is not null)
        {
            foreach (var payment in request.InitialPayments)
            {
                foreach (var pair in ValidatePaymentRequest(payment.Method, payment.Amount, payment.Reference, "initialPayments"))
                {
                    errors[pair.Key] = pair.Value;
                }
            }
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidatePaymentRequest(PaymentMethod method, decimal amount, string? reference, string fieldName)
    {
        var errors = new Dictionary<string, string[]>();
        if (amount <= 0)
        {
            errors[fieldName] = ["Payment amount must be greater than zero."];
        }

        if (method is PaymentMethod.BankTransfer or PaymentMethod.Pos && string.IsNullOrWhiteSpace(reference))
        {
            errors["reference"] = ["Reference is required for transfer and POS payments."];
        }

        return errors;
    }

    private static Task InvalidateSaleRelatedCachesAsync(ApiCacheService cache, Guid tenantId, bool includeInventory, CancellationToken ct)
    {
        var tags = new List<string>
        {
            ApiCacheTags.Sales(tenantId),
            ApiCacheTags.Credits(tenantId),
            ApiCacheTags.Reports(tenantId)
        };

        if (includeInventory)
        {
            tags.Add(ApiCacheTags.Inventory(tenantId));
        }

        return cache.InvalidateTagsAsync(tags, ct);
    }

    private static object BuildSaleSyncPayload(Sale sale)
    {
        return new
        {
            sale.Id,
            sale.SaleNumber,
            Subtotal = sale.Subtotal,
            VatAmount = sale.VatAmount,
            DiscountAmount = sale.DiscountAmount,
            TotalAmount = sale.TotalAmount,
            OutstandingAmount = sale.OutstandingAmount,
            Status = sale.Status.ToString(),
            IsCredit = sale.IsCredit,
            sale.DueDateUtc,
            sale.UpdatedAtUtc
        };
    }
}
