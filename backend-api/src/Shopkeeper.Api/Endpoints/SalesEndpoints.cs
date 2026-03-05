using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class SalesEndpoints
{
    private const string ServerDeviceId = "server-api";

    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sales")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.StaffOrOwner });

        group.MapPost("/", CreateSale);
        group.MapGet("/{id:guid}", GetSale);
        group.MapPost("/{id:guid}/payments", AddPayment);
        group.MapPost("/{id:guid}/void", VoidSale);
        group.MapGet("/{id:guid}/receipt", GetReceipt);

        return app;
    }

    private static async Task<IResult> CreateSale(
        [FromBody] CreateSaleRequest request,
        ShopkeeperDbContext db,
        SaleCalculator calculator,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = ["At least one line is required."] });
        }

        var tenantId = tenant.GetTenantId(httpContext.User);
        var membershipId = tenant.GetMembershipId(httpContext.User);
        if (!tenantId.HasValue || !membershipId.HasValue)
        {
            return Results.Unauthorized();
        }

        var shop = await db.Shops.FirstOrDefaultAsync(x => x.Id == tenantId.Value, ct);
        if (shop is null)
        {
            return Results.NotFound(new { message = "Shop not found." });
        }

        var items = await db.InventoryItems
            .Where(x => x.TenantId == tenantId.Value && request.Lines.Select(l => l.InventoryItemId).Contains(x.Id))
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
        var paidAmount = request.InitialPayments?.Sum(p => p.Amount) ?? 0m;
        var outstanding = Math.Max(0, totals.TotalAmount - paidAmount);

        var saleCount = await db.Sales.CountAsync(x => x.TenantId == tenantId.Value, ct);
        var sale = new Sale
        {
            TenantId = tenantId.Value,
            SaleNumber = $"SL-{DateTime.UtcNow:yyyyMMdd}-{saleCount + 1:0000}",
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
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

        db.Sales.Add(sale);

        if (sale.IsCredit && outstanding > 0)
        {
            db.CreditAccounts.Add(new CreditAccount
            {
                TenantId = tenantId.Value,
                Sale = sale,
                DueDateUtc = request.DueDateUtc ?? DateTime.UtcNow.AddDays(30),
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
            PayloadJson = $"{{\"saleNumber\":\"{sale.SaleNumber}\"}}"
        });
        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = ServerDeviceId,
            EntityName = nameof(Sale),
            EntityId = sale.Id,
            Operation = SyncOperation.Create,
            PayloadJson = JsonSerializer.Serialize(BuildSaleSyncPayload(sale)),
            ClientUpdatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/sales/{sale.Id}", new { sale.Id, sale.SaleNumber, sale.TotalAmount, sale.OutstandingAmount });
    }

    private static async Task<IResult> GetSale(
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
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .Include(x => x.CreditAccount)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);

        if (sale is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            sale.Id,
            sale.SaleNumber,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.Subtotal,
            sale.VatAmount,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.OutstandingAmount,
            Status = sale.Status.ToString(),
            sale.IsCredit,
            sale.DueDateUtc,
            sale.IsVoided,
            sale.UpdatedAtUtc,
            lines = sale.Lines.Select(x => new
            {
                x.Id,
                x.InventoryItemId,
                x.ProductNameSnapshot,
                x.Quantity,
                x.UnitPrice,
                x.LineTotal
            }),
            payments = sale.Payments.Select(x => new
            {
                x.Id,
                Method = x.Method.ToString(),
                x.Amount,
                x.Reference,
                x.CreatedAtUtc
            })
        });
    }

    private static async Task<IResult> AddPayment(
        Guid id,
        [FromBody] AddSalePaymentRequest request,
        ShopkeeperDbContext db,
        CreditLedgerService creditService,
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
            .Include(x => x.CreditAccount)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);

        if (sale is null)
        {
            return Results.NotFound();
        }

        var payment = new SalePayment
        {
            TenantId = tenantId.Value,
            SaleId = sale.Id,
            Method = request.Method,
            Amount = request.Amount,
            Reference = request.Reference
        };

        db.SalePayments.Add(payment);

        sale.OutstandingAmount = Math.Max(0, sale.OutstandingAmount - request.Amount);
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
            DeviceId = ServerDeviceId,
            EntityName = nameof(Sale),
            EntityId = sale.Id,
            Operation = SyncOperation.Update,
            PayloadJson = JsonSerializer.Serialize(BuildSaleSyncPayload(sale)),
            ClientUpdatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            saleId = sale.Id,
            paymentId = payment.Id,
            sale.OutstandingAmount,
            status = sale.Status.ToString()
        });
    }

    private static async Task<IResult> VoidSale(
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
            DeviceId = ServerDeviceId,
            EntityName = nameof(Sale),
            EntityId = sale.Id,
            Operation = SyncOperation.Update,
            PayloadJson = JsonSerializer.Serialize(BuildSaleSyncPayload(sale)),
            ClientUpdatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

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
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == id, ct);

        if (sale is null)
        {
            return Results.NotFound();
        }

        var shop = await db.Shops.FirstAsync(x => x.Id == tenantId.Value, ct);

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
