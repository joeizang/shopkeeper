using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Endpoints;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

Env.TraversePath().Load(".env");
try
{
    Env.NoClobber().TraversePath().Load(".env.local");
}
catch
{
    // .env.local is optional in non-local environments.
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<MagicLinkOptions>(builder.Configuration.GetSection(MagicLinkOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=shopkeeper.db";

builder.Services.AddDbContext<ShopkeeperDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services
    .AddIdentityCore<UserAccount>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<ShopkeeperDbContext>();

builder.Services.AddScoped<AuthTokenService>();
builder.Services.AddScoped<SaleCalculator>();
builder.Services.AddScoped<CreditLedgerService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<MagicLinkService>();
builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddSingleton<ReportDocumentRenderer>();
builder.Services.AddScoped<TenantContextAccessor>();

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
            ctx.User.HasClaim(c => c.Type == CustomClaimTypes.Role && c.Value == MembershipRole.Owner.ToString())));

    options.AddPolicy(AuthPolicyNames.StaffOrOwner, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(c => c.Type == CustomClaimTypes.Role &&
                (c.Value == MembershipRole.Owner.ToString() || c.Value == MembershipRole.Staff.ToString()))));
});

var app = builder.Build();

if (app.Environment.IsEnvironment("Testing"))
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

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
app.MapSyncEndpoints();
app.MapReportsEndpoints();

app.Run();

public partial class Program;
