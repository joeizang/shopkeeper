using Microsoft.Extensions.Options;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Endpoints;

public static class E2ETestEndpoints
{
    private const string AdminTokenHeader = "X-E2E-Admin-Token";

    public static IEndpointRouteBuilder MapE2ETestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/test");
        group.ExcludeFromDescription();

        group.MapPost("/reset-and-seed", ResetAndSeed);
        group.MapGet("/status", GetStatus);
        return app;
    }

    private static async Task<IResult> ResetAndSeed(
        HttpContext httpContext,
        IOptions<E2ETestOptions> options,
        E2ETestSeeder seeder,
        CancellationToken ct)
    {
        var authFailure = Authorize(httpContext, options.Value);
        if (authFailure is not null)
        {
            return authFailure;
        }

        try
        {
            var result = await seeder.ResetAndSeedAsync(ct);
            return TypedResults.Ok(new E2EResetAndSeedResponse(
                result.ShopId,
                result.ShopCode,
                result.OwnerEmail,
                result.ManagerEmail,
                result.SalespersonEmail,
                result.Password,
                result.InventoryProductName,
                result.CreditSaleId,
                result.CreditSaleNumber));
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(title: "E2E reset failed", detail: ex.Message);
        }
    }

    private static IResult GetStatus(
        HttpContext httpContext,
        IOptions<E2ETestOptions> options)
    {
        var authFailure = Authorize(httpContext, options.Value);
        if (authFailure is not null)
        {
            return authFailure;
        }

        return TypedResults.Ok(new E2EStatusResponse(
            httpContext.RequestServices.GetRequiredService<IHostEnvironment>().EnvironmentName,
            AdminTokenHeader,
            true));
    }

    private static IResult? Authorize(HttpContext httpContext, E2ETestOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AdminToken))
        {
            return TypedResults.Problem(title: "E2E admin token is not configured.");
        }

        var provided = httpContext.Request.Headers[AdminTokenHeader].ToString();
        if (!string.Equals(provided, options.AdminToken, StringComparison.Ordinal))
        {
            return TypedResults.Unauthorized();
        }

        return null;
    }

    private sealed record E2EResetAndSeedResponse(
        string ShopId,
        string ShopCode,
        string OwnerEmail,
        string ManagerEmail,
        string SalespersonEmail,
        string Password,
        string InventoryProductName,
        string CreditSaleId,
        string CreditSaleNumber);

    private sealed record E2EStatusResponse(string Environment, string AdminHeader, bool Ready);
}
