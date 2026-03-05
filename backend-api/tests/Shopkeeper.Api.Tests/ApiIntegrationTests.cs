using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Shopkeeper.Api.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task TenantIsolation_BlocksCrossShopInventoryAccess()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var ownerA = await RegisterOwner(client, "owner-a@shopkeeper.local", "Shop A");
        var ownerB = await RegisterOwner(client, "owner-b@shopkeeper.local", "Shop B");

        SetBearer(client, ownerA.AccessToken);
        var createdItem = await CreateInventoryItem(client, "A-Serial-1001");

        SetBearer(client, ownerB.AccessToken);
        var crossShopGet = await client.GetAsync($"/api/v1/inventory/items/{createdItem.Id}");

        Assert.Equal(HttpStatusCode.NotFound, crossShopGet.StatusCode);
    }

    [Fact]
    public async Task SaleTotals_VatAndOutstanding_AreConsistent()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-sales@shopkeeper.local", "Sales Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "S-2001");

        var createSaleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Buyer One",
            customerPhone = "08030000000",
            discountAmount = 100m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 2, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 3000m, reference = (string?)null }
            }
        });
        createSaleResponse.EnsureSuccessStatusCode();
        var saleCreated = (await createSaleResponse.Content.ReadFromJsonAsync<CreateSaleEnvelope>())!;

        Assert.Equal(4200m, saleCreated.TotalAmount);
        Assert.Equal(1200m, saleCreated.OutstandingAmount);

        var saleDetailsResponse = await client.GetAsync($"/api/v1/sales/{saleCreated.Id}");
        saleDetailsResponse.EnsureSuccessStatusCode();
        var saleDetails = (await saleDetailsResponse.Content.ReadFromJsonAsync<SaleDetailsEnvelope>())!;

        Assert.Equal(4000m, saleDetails.Subtotal);
        Assert.Equal(300m, saleDetails.VatAmount);
        Assert.Equal(100m, saleDetails.DiscountAmount);
        Assert.Equal(4200m, saleDetails.TotalAmount);
        Assert.Single(saleDetails.Payments);
        Assert.Equal(3000m, saleDetails.Payments[0].Amount);
    }

    [Fact]
    public async Task CreditRepayments_CloseOut_WhenOutstandingHitsZero()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-credit@shopkeeper.local", "Credit Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "CR-3001");

        var createSaleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Credit Buyer",
            customerPhone = "08031111111",
            discountAmount = 0m,
            isCredit = true,
            dueDateUtc = DateTime.UtcNow.AddDays(14),
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 5000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 1000m, reference = (string?)null }
            }
        });
        createSaleResponse.EnsureSuccessStatusCode();
        var saleCreated = (await createSaleResponse.Content.ReadFromJsonAsync<CreateSaleEnvelope>())!;
        Assert.Equal(4375m, saleCreated.OutstandingAmount);

        var partialRepaymentResponse = await client.PostAsJsonAsync($"/api/v1/credits/{saleCreated.Id}/repayments", new
        {
            amount = 2000m,
            method = 2,
            reference = "TRX-001",
            notes = "partial"
        });
        partialRepaymentResponse.EnsureSuccessStatusCode();

        var creditDetailsResponse = await client.GetAsync($"/api/v1/credits/{saleCreated.Id}");
        creditDetailsResponse.EnsureSuccessStatusCode();
        var creditDetails = (await creditDetailsResponse.Content.ReadFromJsonAsync<CreditDetailsEnvelope>())!;
        Assert.Equal(2375m, creditDetails.Account.OutstandingAmount);

        var finalRepaymentResponse = await client.PostAsJsonAsync($"/api/v1/credits/{saleCreated.Id}/repayments", new
        {
            amount = 2375m,
            method = 1,
            reference = "TRX-002",
            notes = "final"
        });
        finalRepaymentResponse.EnsureSuccessStatusCode();

        var finalCreditResponse = await client.GetAsync($"/api/v1/credits/{saleCreated.Id}");
        finalCreditResponse.EnsureSuccessStatusCode();
        var finalCredit = (await finalCreditResponse.Content.ReadFromJsonAsync<CreditDetailsEnvelope>())!;
        Assert.Equal(0m, finalCredit.Account.OutstandingAmount);
    }

    [Fact]
    public async Task InventoryPatch_WithStaleRowVersion_ReturnsConflict()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-rv@shopkeeper.local", "RowVersion Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "RV-4001");

        var firstUpdateResponse = await client.PatchAsJsonAsync($"/api/v1/inventory/items/{item.Id}", new
        {
            quantity = 5,
            rowVersionBase64 = item.RowVersionBase64
        });
        firstUpdateResponse.EnsureSuccessStatusCode();

        var staleUpdateResponse = await client.PatchAsJsonAsync($"/api/v1/inventory/items/{item.Id}", new
        {
            quantity = 6,
            rowVersionBase64 = item.RowVersionBase64
        });

        Assert.Equal(HttpStatusCode.Conflict, staleUpdateResponse.StatusCode);
    }

    [Fact]
    public async Task OwnerPolicy_DeniesCrossTenantStaffInvite()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var ownerA = await RegisterOwner(client, "owner-invite-a@shopkeeper.local", "Invite Shop A");
        var ownerB = await RegisterOwner(client, "owner-invite-b@shopkeeper.local", "Invite Shop B");

        SetBearer(client, ownerA.AccessToken);
        var inviteResponse = await client.PostAsJsonAsync($"/api/v1/shops/{ownerB.ShopId}/staff/invite", new
        {
            fullName = "Cross Tenant Staff",
            email = "cross-tenant@shopkeeper.local",
            phone = (string?)null,
            temporaryPassword = "Staff1234"
        });

        Assert.Equal(HttpStatusCode.Forbidden, inviteResponse.StatusCode);
    }

    [Fact]
    public async Task MagicLink_RequestAndVerify_RoundTrips_WhenDebugTokenAvailable()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"owner-magic-{suffix}@shopkeeper.local";
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register-owner", new
        {
            fullName = "Magic Owner",
            email,
            phone = (string?)null,
            password = "Shopkeeper123!",
            shopName = $"Magic Shop {suffix}",
            vatEnabled = true,
            vatRate = 0.075m
        });
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthEnvelope>())!;

        var requestMagic = await client.PostAsJsonAsync("/api/v1/auth/magic-link/request", new
        {
            email,
            shopId = auth.ShopId
        });
        Assert.Equal(HttpStatusCode.Accepted, requestMagic.StatusCode);
        var challenge = (await requestMagic.Content.ReadFromJsonAsync<MagicLinkRequestEnvelope>())!;
        Assert.False(string.IsNullOrWhiteSpace(challenge.DebugToken));

        var verifyResponse = await client.PostAsJsonAsync("/api/v1/auth/magic-link/verify", new
        {
            token = challenge.DebugToken,
            shopId = auth.ShopId
        });
        verifyResponse.EnsureSuccessStatusCode();
        var magicAuth = (await verifyResponse.Content.ReadFromJsonAsync<AuthEnvelope>())!;
        Assert.False(string.IsNullOrWhiteSpace(magicAuth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(magicAuth.RefreshToken));
    }

    [Fact]
    public async Task AccountProfile_ReadAndUpdate_Works()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-account@shopkeeper.local", "Account Shop");
        SetBearer(client, owner.AccessToken);

        var meResponse = await client.GetAsync("/api/v1/account/me");
        meResponse.EnsureSuccessStatusCode();
        var me = (await meResponse.Content.ReadFromJsonAsync<AccountProfileEnvelope>())!;
        Assert.Equal("Test Owner", me.FullName);

        var patchResponse = await client.PatchAsJsonAsync("/api/v1/account/me", new
        {
            fullName = "Updated Owner Name",
            phone = "08035550000",
            avatarUrl = "https://example.com/avatar.png",
            preferredLanguage = "en",
            timezone = "Africa/Lagos"
        });
        patchResponse.EnsureSuccessStatusCode();
        var updated = (await patchResponse.Content.ReadFromJsonAsync<AccountProfileEnvelope>())!;
        Assert.Equal("Updated Owner Name", updated.FullName);
        Assert.Equal("08035550000", updated.Phone);
        Assert.Equal("Africa/Lagos", updated.Timezone);
    }

    private static async Task<AuthEnvelope> RegisterOwner(HttpClient client, string email, string shopName)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var emailParts = email.Split('@');
        var uniqueEmail = emailParts.Length == 2
            ? $"{emailParts[0]}-{suffix}@{emailParts[1]}"
            : $"{email}-{suffix}@shopkeeper.local";
        var uniqueShopName = $"{shopName} {suffix}";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register-owner", new
        {
            fullName = "Test Owner",
            email = uniqueEmail,
            phone = (string?)null,
            password = "Shopkeeper123!",
            shopName = uniqueShopName,
            vatEnabled = true,
            vatRate = 0.075m
        });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"RegisterOwner failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
        return (await response.Content.ReadFromJsonAsync<AuthEnvelope>())!;
    }

    private static async Task<InventoryItemEnvelope> CreateInventoryItem(HttpClient client, string serial)
    {
        var response = await client.PostAsJsonAsync("/api/v1/inventory/items", new
        {
            productName = "Used Phone",
            modelNumber = "Pixel-7",
            serialNumber = serial,
            quantity = 10,
            expiryDate = (string?)null,
            costPrice = 1000m,
            sellingPrice = 2000m,
            itemType = 2,
            conditionGrade = 2,
            conditionNotes = "Used but good"
        });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var authHeaders = string.Join(" | ", response.Headers.WwwAuthenticate.Select(x => x.ToString()));
            throw new Xunit.Sdk.XunitException(
                $"CreateInventoryItem failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}. WWW-Authenticate: {authHeaders}");
        }
        return (await response.Content.ReadFromJsonAsync<InventoryItemEnvelope>())!;
    }

    private static void SetBearer(HttpClient client, string accessToken)
    {
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private sealed class ShopkeeperApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shopkeeper-api-tests-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={_dbPath}"
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();

            TryDelete(_dbPath);
            TryDelete($"{_dbPath}-shm");
            TryDelete($"{_dbPath}-wal");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup for test database files.
            }
        }
    }

    private sealed record AuthEnvelope(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc, Guid ShopId, string Role);
    private sealed record InventoryItemEnvelope(Guid Id, string RowVersionBase64);
    private sealed record CreateSaleEnvelope(Guid Id, string SaleNumber, decimal TotalAmount, decimal OutstandingAmount);
    private sealed record SalePaymentEnvelope(Guid Id, decimal Amount, string Method, string? Reference);
    private sealed record SaleDetailsEnvelope(
        Guid Id,
        decimal Subtotal,
        decimal VatAmount,
        decimal DiscountAmount,
        decimal TotalAmount,
        decimal OutstandingAmount,
        List<SalePaymentEnvelope> Payments);
    private sealed record CreditAccountEnvelope(Guid Id, Guid SaleId, DateTime DueDateUtc, decimal OutstandingAmount, int Status);
    private sealed record CreditDetailsEnvelope(CreditAccountEnvelope Account);
    private sealed record MagicLinkRequestEnvelope(Guid RequestId, DateTime ExpiresAtUtc, string Message, string? DebugToken);
    private sealed record AccountProfileEnvelope(
        Guid UserId,
        string FullName,
        string? Email,
        string? Phone,
        string? AvatarUrl,
        string? PreferredLanguage,
        string? Timezone,
        DateTime CreatedAtUtc);
}
