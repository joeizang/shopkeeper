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

public static class CreditEndpoints
{
    private const string ServerDeviceId = "server-api";

    public static IEndpointRouteBuilder MapCreditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/credits")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.SalesAccess });

        group.MapGet("/", ListCredits);
        group.MapGet("/{saleId:guid}", GetCredit);
        group.MapPost("/{saleId:guid}/repayments", AddRepayment);

        return app;
    }

    private static async Task<IResult> ListCredits(
        [FromQuery] int page,
        [FromQuery] int limit,
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

        var effectivePage = Math.Max(1, page == 0 ? 1 : page);
        var effectiveLimit = Math.Clamp(limit == 0 ? 100 : limit, 1, 200);

        var query = db.CreditAccounts.Where(x => x.TenantId == tenantId.Value);
        var total = await query.CountAsync(ct);
        var credits = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectiveLimit)
            .Take(effectiveLimit)
            .Select(x => new CreditAccountView(x.Id, x.SaleId, x.DueDateUtc, x.OutstandingAmount, x.Status))
            .ToListAsync(ct);

        return Results.Ok(new { total, page = effectivePage, limit = effectiveLimit, items = credits });
    }

    private static async Task<IResult> GetCredit(
        Guid saleId,
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

        var credit = await db.CreditAccounts
            .Include(x => x.Repayments)
                .ThenInclude(x => x.SalePayment)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.SaleId == saleId, ct);

        if (credit is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new CreditDetailResponse(
            new CreditAccountView(credit.Id, credit.SaleId, credit.DueDateUtc, credit.OutstandingAmount, credit.Status),
            credit.Repayments
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new CreditRepaymentView(
                    r.Id,
                    r.Amount,
                    r.SalePayment.Method,
                    r.SalePayment.Reference,
                    r.Notes,
                    r.CreatedAtUtc))
                .ToList()));
    }

    private static async Task<IResult> AddRepayment(
        Guid saleId,
        [FromBody] CreditRepaymentRequest request,
        ShopkeeperDbContext db,
        CreditLedgerService service,
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
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value && x.Id == saleId, ct);

        if (sale?.CreditAccount is null)
        {
            return Results.NotFound(new { message = "Credit account not found for sale." });
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

        service.ApplyRepayment(sale.CreditAccount, request.Amount);
        sale.OutstandingAmount = sale.CreditAccount.OutstandingAmount;
        sale.Status = sale.OutstandingAmount == 0 ? SaleStatus.Completed : SaleStatus.PartiallyPaid;

        db.CreditRepayments.Add(new CreditRepayment
        {
            TenantId = tenantId.Value,
            CreditAccountId = sale.CreditAccount.Id,
            SalePayment = payment,
            Amount = request.Amount,
            Notes = request.Notes
        });
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId.Value,
            UserAccountId = tenant.GetUserId(httpContext.User),
            Action = "credits.repayment.add",
            EntityName = nameof(CreditRepayment),
            EntityId = payment.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                sale.Id,
                request.Amount,
                request.Method,
                request.Reference
            })
        });
        db.SyncChanges.Add(new SyncChange
        {
            TenantId = tenantId.Value,
            DeviceId = ServerDeviceId,
            EntityName = nameof(Sale),
            EntityId = sale.Id,
            Operation = SyncOperation.Update,
            PayloadJson = SyncJson.Serialize(new
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
            }),
            ClientUpdatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            saleId = sale.Id,
            creditId = sale.CreditAccount.Id,
            outstanding = sale.OutstandingAmount,
            status = sale.CreditAccount.Status.ToString()
        });
    }
}
