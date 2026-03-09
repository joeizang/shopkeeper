using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class ExpensesEndpoints
{
    public static IEndpointRouteBuilder MapExpensesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/expenses")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });

        group.MapGet("/", ListExpenses);
        group.MapPost("/", CreateExpense);
        group.MapPatch("/{id:guid}", UpdateExpense);
        group.MapDelete("/{id:guid}", DeleteExpense);

        return app;
    }

    private static async Task<IResult> ListExpenses(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var fromUtc = DateRangeParser.ParseDateStart(from);
        var toUtc = DateRangeParser.ParseDateEnd(to);
        if ((from is not null && !fromUtc.HasValue) || (to is not null && !toUtc.HasValue))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["Invalid date format. Use YYYY-MM-DD."] });
        }

        var cached = await reads.GetExpensesAsync(tenantId.Value, fromUtc, toUtc, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> CreateExpense(
        [FromBody] CreateExpenseRequest request,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
        }

        if (request.Amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount must be greater than zero."] });
        }

        var created = await reporting.CreateExpense(tenantId.Value, tenant.GetUserId(httpContext.User), request, ct);
        return Results.Created($"/api/v1/expenses/{created.Id}", created);
    }

    private static async Task<IResult> UpdateExpense(
        Guid id,
        [FromBody] UpdateExpenseRequest request,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.RowVersionBase64))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "Precondition required", detail: "rowVersionBase64 is required for expense updates.");
        }

        if (request.Amount.HasValue && request.Amount.Value <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount must be greater than zero."] });
        }

        var result = await reporting.UpdateExpense(tenantId.Value, id, request, ct);
        return result.Status switch
        {
            UpdateExpenseStatus.NotFound => Results.NotFound(),
            UpdateExpenseStatus.Conflict => Results.Conflict(new { message = "Expense has changed. Refresh and try again." }),
            _ => Results.Ok(result.View)
        };
    }

    private static async Task<IResult> DeleteExpense(
        Guid id,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var deleted = await reporting.DeleteExpense(tenantId.Value, id, ct);
        return deleted ? Results.Ok(new { id, status = "deleted" }) : Results.NotFound();
    }

}
