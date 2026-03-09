using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using dotenv.net;
using NodaTime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Endpoints;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;
using ZiggyCreatures.Caching.Fusion;

DotEnv.Load(options: new DotEnvOptions(envFilePaths: ["./.env", "./.env.local"]));
var envs = DotEnv.Read();

var builder = WebApplication.CreateBuilder(args);
// builder.Configuration.AddEnvironmentVariables();

builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

var httpsRedirectionEnabled = builder.Configuration.GetValue<bool?>("AppHttpsRedirectionEnabled")
    ?? !builder.Environment.IsDevelopment();

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    NodaTime.Serialization.SystemTextJson.Extensions
        .ConfigureForNodaTime(options.SerializerOptions, DateTimeZoneProviders.Tzdb));
builder.Services.AddFusionCache()
    .WithDefaultEntryOptions(options =>
    {
        options.Duration = TimeSpan.FromMinutes(1);
        options.IsFailSafeEnabled = true;
        options.FailSafeMaxDuration = TimeSpan.FromMinutes(5);
        options.FactorySoftTimeout = TimeSpan.FromSeconds(2);
        options.FactoryHardTimeout = TimeSpan.FromSeconds(10);
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod |
                            HttpLoggingFields.RequestPath |
                            HttpLoggingFields.ResponseStatusCode |
                            HttpLoggingFields.Duration;
    options.RequestHeaders.Add("X-Correlation-Id");
    options.RequestHeaders.Add("X-Device-Id");
    options.ResponseHeaders.Add("X-Correlation-Id");
});

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "JwtIssuer is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "JwtAudience is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= 32, "JwtSigningKey must be configured with at least 32 characters.")
    .Validate(o => o.AccessTokenMinutes is >= 5 and <= 1440, "JwtAccessTokenMinutes must be between 5 and 1440.")
    .Validate(o => o.RefreshTokenDays is >= 1 and <= 365, "JwtRefreshTokenDays must be between 1 and 365.")
    .ValidateOnStart();

builder.Services.AddOptions<MagicLinkOptions>()
    .Bind(builder.Configuration.GetSection(MagicLinkOptions.SectionName))
    .Validate(o => o.ExpiryMinutes is >= 5 and <= 60, "MagicLinkExpiryMinutes must be between 5 and 60.")
    .Validate(o => o.MaxRequestsPerMinutePerEmail is >= 1 and <= 20, "MagicLinkMaxRequestsPerMinutePerEmail must be between 1 and 20.")
    .Validate(o => Uri.TryCreate(o.AppLinkBaseUrl, UriKind.Absolute, out _), "MagicLinkAppLinkBaseUrl must be an absolute URL.")
    .ValidateOnStart();

builder.Services.AddOptions<GoogleAuthOptions>()
    .Bind(builder.Configuration.GetSection(GoogleAuthOptions.SectionName))
    .Validate(o => o.AllowedAudiences.Length == 0 || o.AllowedAudiences.All(a => a.Length > 10), "GoogleAllowedAudiences contains an entry that appears invalid.")
    .ValidateOnStart();

var connectionString = envs["DbConnectionString"]
    ?? "Host=192.168.0.5;Port=5432;Database=shopkeeper;Username=postgres;Password=postgres-2026";

var test = connectionString;

builder.Services.AddDbContext<ShopkeeperDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseNodaTime()));

builder.Services
    .AddIdentityCore<UserAccount>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ShopkeeperDbContext>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    // Restrict to only trust forwarded headers from
    // the local Caddy instance (loopback). This prevents
    // header spoofing from external clients.
    options.KnownIPNetworks?.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);      // 127.0.0.1
    options.KnownProxies.Add(IPAddress.IPv6Loopback);  // ::1
});

builder.Services.AddScoped<AuthTokenService>();
builder.Services.AddScoped<ApiCacheService>();
builder.Services.AddScoped<AccountReadService>();
builder.Services.AddScoped<ShopReadService>();
builder.Services.AddScoped<InventoryReadService>();
builder.Services.AddScoped<SaleReadService>();
builder.Services.AddScoped<CreditReadService>();
builder.Services.AddScoped<SaleCalculator>();
builder.Services.AddScoped<CreditLedgerService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<ReportingReadService>();
builder.Services.AddScoped<MagicLinkService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddSingleton<ReportDocumentRenderer>();
builder.Services.AddSingleton<ReportJobChannel>();
builder.Services.AddHostedService<ReportJobWorker>();
builder.Services.AddScoped<TenantContextAccessor>();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API is running."), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();

if (!builder.Environment.IsEnvironment("Testing")
    && (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32))
{
    throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 characters.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicyNames.OwnerOnly, policy =>
        policy.RequireAssertion(ctx =>
        {
            var role = RoleCapabilities.GetRole(ctx.User);
            return role.HasValue && RoleCapabilities.IsOwner(role.Value);
        }));

    options.AddPolicy(AuthPolicyNames.OwnerOrManager, policy =>
        policy.RequireAssertion(ctx =>
        {
            var role = RoleCapabilities.GetRole(ctx.User);
            return role.HasValue && RoleCapabilities.IsManagerOrOwner(role.Value);
        }));

    options.AddPolicy(AuthPolicyNames.SalesAccess, policy =>
        policy.RequireAssertion(ctx =>
        {
            var role = RoleCapabilities.GetRole(ctx.User);
            return role.HasValue && RoleCapabilities.CanManageSales(role.Value);
        }));

    options.AddPolicy(AuthPolicyNames.ReportingAccess, policy =>
        policy.RequireAssertion(ctx =>
        {
            var role = RoleCapabilities.GetRole(ctx.User);
            return role.HasValue && RoleCapabilities.CanViewReports(role.Value);
        }));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddSlidingWindowLimiter("auth", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
        o.QueueLimit = 0;
    });

    options.AddSlidingWindowLimiter("sync", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
        o.QueueLimit = 0;
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetTokenBucketLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 200,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                TokensPerPeriod = 50,
                QueueLimit = 0
            }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins("https://placeholder.invalid");
        }
    });
});

// ── Enable HTTP/2 (and optionally HTTP/3) on Kestrel ──
builder.WebHost.ConfigureKestrel(options =>
{
    // Listen on loopback only — Caddy is the public face.
    // Never expose Kestrel directly on 0.0.0.0 in production.
    options.Listen(IPAddress.Loopback, 5000, listenOptions =>
    {
        // h2c = HTTP/2 cleartext (no TLS on this hop,
        // Caddy handles TLS termination)
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

var app = builder.Build();

// ForwardedHeaders MUST be the first middleware —
// everything downstream depends on the corrected values
app.UseForwardedHeaders();

// Capture the logger once at startup — not per-request — to avoid
// resolving ILoggerFactory from the DI container on every request.
var requestLogger = app.Logger;

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    ctx.Response.Headers["X-Correlation-Id"] = correlationId;
    ctx.Items["CorrelationId"] = correlationId;

    using var scope = requestLogger.BeginScope(new Dictionary<string, object?>
    {
        ["CorrelationId"] = correlationId,
        ["TraceId"] = Activity.Current?.TraceId.ToString(),
        ["TenantId"] = ctx.User.FindFirst(CustomClaimTypes.TenantId)?.Value,
        ["MembershipId"] = ctx.User.FindFirst(CustomClaimTypes.MembershipId)?.Value,
        ["UserId"] = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    });

    var sw = Stopwatch.StartNew();
    try
    {
        await next();
        requestLogger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms",
            ctx.Request.Method,
            ctx.Request.Path,
            ctx.Response.StatusCode,
            sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        requestLogger.LogError(ex, "HTTP {Method} {Path} failed after {ElapsedMs} ms",
            ctx.Request.Method, ctx.Request.Path, sw.ElapsedMilliseconds);
        throw;
    }
});

if (app.Environment.IsEnvironment("Testing"))
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

if (!app.Environment.IsProduction() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsEnvironment("Testing") && httpsRedirectionEnabled)
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseHttpLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 200, [HealthStatus.Unhealthy] = 503 }
}).RequireRateLimiting("auth");
app.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 200, [HealthStatus.Unhealthy] = 503 }
}).RequireRateLimiting("auth");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopkeeperDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
    if (app.Environment.IsEnvironment("Testing"))
    {
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        await DevelopmentSeeder.SeedAsync(db, userManager);
    }
}

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok", service = "shopkeeper-api" }));

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapShopEndpoints();
app.MapInventoryEndpoints();
app.MapSalesEndpoints();
app.MapCreditEndpoints();
app.MapExpensesEndpoints();
app.MapSyncEndpoints();
app.MapReportsEndpoints();

app.Run();

public partial class Program;

internal sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopkeeperDbContext>();
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to database.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
