using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Endpoints;
using Shopkeeper.Api.Infrastructure;
using Shopkeeper.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=shopkeeper.db";

builder.Services.AddDbContext<ShopkeeperDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<PasswordHasher>();
builder.Services.AddScoped<AuthTokenService>();
builder.Services.AddScoped<SaleCalculator>();
builder.Services.AddScoped<CreditLedgerService>();
builder.Services.AddScoped<TenantContextAccessor>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
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

app.UseExceptionHandler();

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
    var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
    {
        await DevelopmentSeeder.SeedAsync(db, hasher);
    }
}

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok", service = "shopkeeper-api" }));

app.MapAuthEndpoints();
app.MapShopEndpoints();
app.MapInventoryEndpoints();
app.MapSalesEndpoints();
app.MapCreditEndpoints();
app.MapSyncEndpoints();

app.Run();

public partial class Program;
