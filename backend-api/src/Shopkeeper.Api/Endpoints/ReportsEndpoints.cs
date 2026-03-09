using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Endpoints;

public static class ReportsEndpoints
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports")
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.ReportingAccess });

        group.MapGet("/inventory", GetInventoryReport);
        group.MapGet("/sales", GetSalesReport);
        group.MapGet("/profit-loss", GetProfitLossReport)
            .RequireAuthorization(new AuthorizeAttribute { Policy = AuthPolicyNames.OwnerOnly });
        group.MapGet("/creditors", GetCreditorsReport);
        group.MapPost("/jobs", QueueReportJob);
        group.MapGet("/jobs", GetReportJobs);
        group.MapGet("/jobs/{id:guid}", GetReportJob);
        group.MapPost("/jobs/{id:guid}/retry", RetryReportJob);
        group.MapGet("/files", GetReportFiles);
        group.MapGet("/files/{id:guid}/download", DownloadReportFile);
        group.MapGet("/{reportType}/export", ExportReport);

        return app;
    }

    private static async Task<IResult> GetInventoryReport(
        ReportingReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var cached = await reads.GetInventoryReportAsync(tenantId.Value, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> GetSalesReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var range = DateRangeParser.Resolve(from, to);
        if (range.error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
        }

        var cached = await reads.GetSalesReportAsync(tenantId.Value, range.fromUtc, range.toUtc, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> GetProfitLossReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var range = DateRangeParser.Resolve(from, to);
        if (range.error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
        }

        var cached = await reads.GetProfitLossReportAsync(tenantId.Value, range.fromUtc, range.toUtc, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> GetCreditorsReport(
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

        var cached = await reads.GetCreditorsReportAsync(tenantId.Value, fromUtc, toUtc, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> ExportReport(
        string reportType,
        [FromQuery] string? format,
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        var userId = tenant.GetUserId(httpContext.User);
        if (!tenantId.HasValue || !userId.HasValue) return Results.Unauthorized();

        var normalizedFormat = format?.Trim().ToLowerInvariant();
        if (normalizedFormat is not ("pdf" or "spreadsheet" or "csv" or "xlsx"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["format"] = ["Use pdf or spreadsheet."] });
        }
        var canonicalFormat = normalizedFormat is "pdf" ? "pdf" : "spreadsheet";

        var normalizedType = reportType.Trim().ToLowerInvariant();
        if (normalizedType is not ("inventory" or "sales" or "profit-loss" or "creditors"))
        {
            return Results.NotFound(new { message = $"Unknown report type '{reportType}'." });
        }

        if (normalizedType == "profit-loss" &&
            !RoleCapabilities.IsOwner(RoleCapabilities.GetRole(httpContext.User) ?? MembershipRole.Salesperson))
        {
            return Results.Forbid();
        }

        Instant? fromUtc = null;
        Instant? toUtc = null;
        if (normalizedType is "sales" or "profit-loss")
        {
            var range = DateRangeParser.Resolve(from, to);
            if (range.error is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
            }
            fromUtc = range.fromUtc;
            toUtc = range.toUtc;
        }
        else if (normalizedType == "creditors")
        {
            fromUtc = DateRangeParser.ParseDateStart(from);
            toUtc = DateRangeParser.ParseDateEnd(to);
            if ((from is not null && !fromUtc.HasValue) || (to is not null && !toUtc.HasValue))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["Invalid date format. Use YYYY-MM-DD."] });
            }
        }

        var job = await reporting.QueueReportJob(tenantId.Value, userId.Value, normalizedType, canonicalFormat, fromUtc, toUtc, ct);
        return Results.Accepted($"/api/v1/reports/jobs/{job.Id}", job);
    }

    private static async Task<IResult> GetReportJobs(
        ReportingReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var cached = await reads.GetReportJobsAsync(tenantId.Value, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> QueueReportJob(
        [FromBody] QueueReportJobRequest request,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        var userId = tenant.GetUserId(httpContext.User);
        if (!tenantId.HasValue || !userId.HasValue) return Results.Unauthorized();

        var normalizedType = request.ReportType.Trim().ToLowerInvariant();
        var normalizedFormat = request.Format.Trim().ToLowerInvariant();
        if (normalizedFormat is not ("pdf" or "spreadsheet" or "csv" or "xlsx"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["format"] = ["Use pdf, spreadsheet, csv, or xlsx."] });
        }
        var canonicalFormat = normalizedFormat is "pdf" ? "pdf" : "spreadsheet";

        if (normalizedType is not ("inventory" or "sales" or "profit-loss" or "creditors"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reportType"] = ["Unsupported report type."] });
        }

        if (normalizedType == "profit-loss" &&
            !RoleCapabilities.IsOwner(RoleCapabilities.GetRole(httpContext.User) ?? MembershipRole.Salesperson))
        {
            return Results.Forbid();
        }

        var job = await reporting.QueueReportJob(
            tenantId.Value,
            userId.Value,
            normalizedType,
            canonicalFormat,
            request.FromUtc,
            request.ToUtc,
            ct);

        return Results.Accepted($"/api/v1/reports/jobs/{job.Id}", job);
    }

    private static async Task<IResult> GetReportJob(
        Guid id,
        ReportingReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var cached = await reads.GetReportJobAsync(tenantId.Value, id, ct);
        return cached.Value is null
            ? Results.NotFound()
            : HttpCacheResults.OkOrNotModified(httpContext, cached!);
    }

    private static async Task<IResult> RetryReportJob(
        Guid id,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var retried = await reporting.RetryReportJob(tenantId.Value, id, ct);
        return retried is null
            ? Results.NotFound()
            : Results.Accepted($"/api/v1/reports/jobs/{retried.Id}", retried);
    }

    private static async Task<IResult> GetReportFiles(
        ReportingReadService reads,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var cached = await reads.GetReportFilesAsync(tenantId.Value, ct);
        return HttpCacheResults.OkOrNotModified(httpContext, cached);
    }

    private static async Task<IResult> DownloadReportFile(
        Guid id,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var file = await reporting.GetReportFile(tenantId.Value, id, ct);
        if (file is null)
        {
            return Results.NotFound();
        }

        return HttpCacheResults.FileOrNotModified(
            httpContext,
            file.Content,
            file.ContentType,
            file.FileName,
            ETagUtility.CreateStrong(file.Content));
    }

}
