using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var report = await reporting.BuildInventoryReport(tenantId.Value, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetSalesReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var range = ResolveDateRange(from, to);
        if (range.error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
        }

        var report = await reporting.BuildSalesReport(tenantId.Value, range.fromUtc, range.toUtc, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetProfitLossReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var range = ResolveDateRange(from, to);
        if (range.error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
        }

        var report = await reporting.BuildProfitLossReport(tenantId.Value, range.fromUtc, range.toUtc, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetCreditorsReport(
        [FromQuery] string? from,
        [FromQuery] string? to,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var fromUtc = ParseDateStart(from);
        var toUtc = ParseDateEnd(to);
        if ((from is not null && !fromUtc.HasValue) || (to is not null && !toUtc.HasValue))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["Invalid date format. Use YYYY-MM-DD."] });
        }

        var report = await reporting.BuildCreditorsReport(tenantId.Value, fromUtc, toUtc, ct);
        return Results.Ok(report);
    }

    // ExportReport queues an async job and returns 202 Accepted with the job ID.
    // Poll GET /api/v1/reports/jobs/{id} until Status == "Completed", then download
    // the file via GET /api/v1/reports/files/{fileId}/download.
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

        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        if (normalizedType is "sales" or "profit-loss")
        {
            var range = ResolveDateRange(from, to);
            if (range.error is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = [range.error] });
            }
            fromUtc = range.fromUtc;
            toUtc = range.toUtc;
        }
        else if (normalizedType == "creditors")
        {
            fromUtc = ParseDateStart(from);
            toUtc = ParseDateEnd(to);
            if ((from is not null && !fromUtc.HasValue) || (to is not null && !toUtc.HasValue))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["Invalid date format. Use YYYY-MM-DD."] });
            }
        }

        var job = await reporting.QueueReportJob(tenantId.Value, userId.Value, normalizedType, canonicalFormat, fromUtc, toUtc, ct);
        return Results.Accepted($"/api/v1/reports/jobs/{job.Id}", job);
    }

    private static async Task<IResult> GetReportJobs(
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var jobs = await reporting.ListReportJobs(tenantId.Value, ct);
        return Results.Ok(jobs);
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
        if (normalizedFormat is not ("pdf" or "spreadsheet"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["format"] = ["Use pdf or spreadsheet."] });
        }

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
            normalizedFormat,
            request.FromUtc,
            request.ToUtc,
            ct);

        return Results.Accepted($"/api/v1/reports/jobs/{job.Id}", job);
    }

    private static async Task<IResult> GetReportJob(
        Guid id,
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var job = await reporting.GetReportJob(tenantId.Value, id, ct);
        return job is null
            ? Results.NotFound()
            : Results.Ok(new ReportJobView(job.Id, job.ReportType, job.Format, job.Status, job.FilterJson, job.ReportFileId, job.RequestedAtUtc, job.CompletedAtUtc, job.FailureReason));
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
        ReportingService reporting,
        TenantContextAccessor tenant,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId(httpContext.User);
        if (!tenantId.HasValue) return Results.Unauthorized();

        var files = await reporting.ListReportFiles(tenantId.Value, ct);
        return Results.Ok(files);
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
        return file is null
            ? Results.NotFound()
            : Results.File(file.Content, file.ContentType, file.FileName);
    }

    private static (DateTime fromUtc, DateTime toUtc, string? error) ResolveDateRange(string? fromRaw, string? toRaw)
    {
        var defaultFrom = DateTime.UtcNow.Date.AddDays(-30);
        var defaultTo = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
        var from = ParseDateStart(fromRaw) ?? defaultFrom;
        var to = ParseDateEnd(toRaw) ?? defaultTo;
        if (from > to)
        {
            return (defaultFrom, defaultTo, "'from' date must be before or equal to 'to' date.");
        }

        return (from, to, null);
    }

    private static DateTime? ParseDateStart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParse(value, out var dateOnly))
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }
        return null;
    }

    private static DateTime? ParseDateEnd(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParse(value, out var dateOnly))
        {
            return dateOnly.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        }
        return null;
    }

    private static ExportKind? NormalizeFormat(string? format)
    {
        return format?.Trim().ToLowerInvariant() switch
        {
            "pdf" => ExportKind.Pdf,
            "spreadsheet" => ExportKind.Spreadsheet,
            "csv" => ExportKind.Spreadsheet,
            "xlsx" => ExportKind.Spreadsheet,
            _ => null
        };
    }

    private enum ExportKind
    {
        Pdf,
        Spreadsheet
    }
}
