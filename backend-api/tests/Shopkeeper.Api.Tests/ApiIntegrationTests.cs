using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Shopkeeper.Api.Data;
using Shopkeeper.Api.Domain;
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
            temporaryPassword = "Staff1234",
            role = "ShopManager"
        });

        Assert.Equal(HttpStatusCode.Forbidden, inviteResponse.StatusCode);
    }

    [Fact]
    public async Task Salesperson_CannotAccessInventoryOrReports_ButCanCreateSales()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-sales-role@shopkeeper.local", "Role Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "ROLE-1001");
        var salesperson = await InviteStaffAndLogin(client, owner.ShopId, "salesperson@shopkeeper.local", "Salesperson", "Salesperson");
        SetBearer(client, salesperson.AccessToken);

        var inventoryResponse = await client.PostAsJsonAsync("/api/v1/inventory/items", new
        {
            productName = "Blocked Item",
            modelNumber = "B-1",
            serialNumber = "BLOCKED-1",
            quantity = 1,
            expiryDate = (string?)null,
            costPrice = 10m,
            sellingPrice = 20m,
            itemType = 1,
            conditionGrade = (int?)null,
            conditionNotes = (string?)null
        });
        Assert.Equal(HttpStatusCode.Forbidden, inventoryResponse.StatusCode);

        var reportsResponse = await client.GetAsync("/api/v1/reports/inventory");
        Assert.Equal(HttpStatusCode.Forbidden, reportsResponse.StatusCode);

        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Walk In",
            customerPhone = "08032222222",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 2150m, reference = (string?)null }
            }
        });
        saleResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ShopManager_CanManageInventory_ButCannotUpdateOwnerOnlySettings()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-manager-role@shopkeeper.local", "Manager Shop");
        SetBearer(client, owner.AccessToken);

        var manager = await InviteStaffAndLogin(client, owner.ShopId, "manager@shopkeeper.local", "ShopManager", "Manager");
        SetBearer(client, manager.AccessToken);

        var inventoryResponse = await client.PostAsJsonAsync("/api/v1/inventory/items", new
        {
            productName = "Manager Added Item",
            modelNumber = "M-1",
            serialNumber = "MANAGER-1",
            quantity = 2,
            expiryDate = (string?)null,
            costPrice = 100m,
            sellingPrice = 220m,
            itemType = 2,
            conditionGrade = 2,
            conditionNotes = "Managed"
        });
        inventoryResponse.EnsureSuccessStatusCode();

        var patchSettingsResponse = await client.PatchAsJsonAsync($"/api/v1/shops/{owner.ShopId}/settings", new
        {
            vatEnabled = true,
            vatRate = 0.1m,
            defaultDiscountPercent = 0.05m,
            rowVersionBase64 = (string?)null
        });
        Assert.Equal(HttpStatusCode.Forbidden, patchSettingsResponse.StatusCode);
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

    [Fact]
    public async Task Owner_CanUpdateShopVatSettings()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-vat@shopkeeper.local", "Vat Shop");
        SetBearer(client, owner.AccessToken);

        var shopsResponse = await client.GetAsync("/api/v1/shops/me");
        shopsResponse.EnsureSuccessStatusCode();
        var shops = (await shopsResponse.Content.ReadFromJsonAsync<List<ShopEnvelope>>())!;
        var shop = Assert.Single(shops);

        var patchResponse = await client.PatchAsJsonAsync($"/api/v1/shops/{shop.Id}/settings", new
        {
            vatEnabled = true,
            vatRate = 0.1m,
            defaultDiscountPercent = 0.05m,
            rowVersionBase64 = shop.RowVersionBase64
        });
        patchResponse.EnsureSuccessStatusCode();

        var updated = (await patchResponse.Content.ReadFromJsonAsync<ShopEnvelope>())!;
        Assert.True(updated.VatEnabled);
        Assert.Equal(0.1m, updated.VatRate);
        Assert.Equal(0.05m, updated.DefaultDiscountPercent);
        Assert.NotEqual(shop.RowVersionBase64, updated.RowVersionBase64);
    }

    [Fact]
    public async Task ProfitLoss_UsesRecordedExpenses_ForNetProfit()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-profit@shopkeeper.local", "Profit Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "PL-5001");

        var createSaleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Profit Buyer",
            customerPhone = "08032222222",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 2, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 4300m, reference = "CASH-PL" }
            }
        });
        createSaleResponse.EnsureSuccessStatusCode();

        var expenseResponse = await client.PostAsJsonAsync("/api/v1/expenses/", new
        {
            title = "Shop Rent",
            category = "Operations",
            amount = 500m,
            expenseDateUtc = DateTime.UtcNow.Date
        });
        expenseResponse.EnsureSuccessStatusCode();

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var profitLossResponse = await client.GetAsync($"/api/v1/reports/profit-loss?from={today}&to={today}");
        profitLossResponse.EnsureSuccessStatusCode();
        var report = (await profitLossResponse.Content.ReadFromJsonAsync<ProfitLossEnvelope>())!;

        Assert.Equal(4300m, report.Revenue);
        Assert.Equal(2000m, report.Cogs);
        Assert.Equal(2300m, report.GrossProfit);
        Assert.Equal(500m, report.Expenses);
        Assert.Equal(1800m, report.NetProfitLoss);
        Assert.Contains(report.ExpenseBreakdown, x => x.Category == "Operations" && x.Amount == 500m);
    }

    [Fact]
    public async Task ReportExport_PersistsJobAndFile_AndSupportsDownload()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-reports@shopkeeper.local", "Reports Shop");
        SetBearer(client, owner.AccessToken);

        await CreateInventoryItem(client, "RP-6001");

        var exportResponse = await client.GetAsync("/api/v1/reports/inventory/export?format=spreadsheet");
        exportResponse.EnsureSuccessStatusCode();

        var filesResponse = await client.GetAsync("/api/v1/reports/files");
        filesResponse.EnsureSuccessStatusCode();
        var files = (await filesResponse.Content.ReadFromJsonAsync<List<ReportFileEnvelope>>())!;
        Assert.NotEmpty(files);

        var jobsResponse = await client.GetAsync("/api/v1/reports/jobs");
        jobsResponse.EnsureSuccessStatusCode();
        var jobs = (await jobsResponse.Content.ReadFromJsonAsync<List<ReportJobEnvelope>>())!;
        Assert.NotEmpty(jobs);
        Assert.Contains(jobs, x => x.ReportFileId == files[0].Id && x.Status == "Completed");

        var downloadResponse = await client.GetAsync($"/api/v1/reports/files/{files[0].Id}/download");
        downloadResponse.EnsureSuccessStatusCode();
        Assert.True((await downloadResponse.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task RevokeAllSessions_RevokesEveryActiveSession()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-sessions@shopkeeper.local", "Session Shop");
        SetBearer(client, owner.AccessToken);

        var secondLoginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login = owner.Email,
            password = "Shopkeeper123!",
            shopId = owner.ShopId
        });
        secondLoginResponse.EnsureSuccessStatusCode();

        var revokeAllResponse = await client.PostAsync("/api/v1/account/sessions/revoke-all", content: null);
        revokeAllResponse.EnsureSuccessStatusCode();

        var sessionsResponse = await client.GetAsync("/api/v1/account/sessions");
        sessionsResponse.EnsureSuccessStatusCode();
        var sessions = (await sessionsResponse.Content.ReadFromJsonAsync<List<SessionEnvelope>>())!;

        Assert.NotEmpty(sessions);
        Assert.All(sessions, session => Assert.True(session.IsRevoked));
    }

    [Fact]
    public async Task QueuedReportJob_CompletesAndProducesDownloadableFile()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-queued-reports@shopkeeper.local", "Queued Reports Shop");
        SetBearer(client, owner.AccessToken);

        await CreateInventoryItem(client, "QR-7001");

        var queueResponse = await client.PostAsJsonAsync("/api/v1/reports/jobs", new
        {
            reportType = "inventory",
            format = "spreadsheet",
            fromUtc = (DateTime?)null,
            toUtc = (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Accepted, queueResponse.StatusCode);
        var queuedJob = (await queueResponse.Content.ReadFromJsonAsync<ReportJobEnvelope>())!;

        ReportJobEnvelope? completed = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(300);
            var getResponse = await client.GetAsync($"/api/v1/reports/jobs/{queuedJob.Id}");
            getResponse.EnsureSuccessStatusCode();
            completed = await getResponse.Content.ReadFromJsonAsync<ReportJobEnvelope>();
            if (completed?.Status == "Completed" && completed.ReportFileId.HasValue)
            {
                break;
            }
        }

        Assert.NotNull(completed);
        Assert.Equal("Completed", completed!.Status);
        Assert.NotNull(completed.ReportFileId);

        var downloadResponse = await client.GetAsync($"/api/v1/reports/files/{completed.ReportFileId}/download");
        downloadResponse.EnsureSuccessStatusCode();
        Assert.True((await downloadResponse.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task SyncPush_UpdateAndDeleteInventoryItem_MutatesServerState()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-sync@shopkeeper.local", "Sync Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "SYNC-8001");

        var updatePayload = """
            {
              "productName": "Updated Used Phone",
              "modelNumber": "Pixel-7",
              "serialNumber": "SYNC-8001",
              "quantity": 3,
              "expiryDate": null,
              "costPrice": 1000,
              "sellingPrice": 2200,
              "itemType": 2,
              "conditionGrade": 2,
              "conditionNotes": "Updated offline"
            }
            """;

        var updateSyncResponse = await client.PostAsJsonAsync("/api/v1/sync/push", new
        {
            changes = new[]
            {
                new
                {
                    deviceId = "test-device",
                    entityName = "InventoryItem",
                    entityId = item.Id,
                    operation = 2,
                    payloadJson = updatePayload,
                    clientUpdatedAtUtc = DateTime.UtcNow,
                    rowVersionBase64 = item.RowVersionBase64
                }
            }
        });
        updateSyncResponse.EnsureSuccessStatusCode();

        var updatedItemResponse = await client.GetAsync($"/api/v1/inventory/items/{item.Id}");
        updatedItemResponse.EnsureSuccessStatusCode();
        var updatedItem = (await updatedItemResponse.Content.ReadFromJsonAsync<InventoryItemEnvelope>())!;
        Assert.Equal("Updated Used Phone", updatedItem.ProductName);
        Assert.Equal(3, updatedItem.Quantity);

        var deleteSyncResponse = await client.PostAsJsonAsync("/api/v1/sync/push", new
        {
            changes = new[]
            {
                new
                {
                    deviceId = "test-device",
                    entityName = "InventoryItem",
                    entityId = item.Id,
                    operation = 3,
                    payloadJson = "{}",
                    clientUpdatedAtUtc = DateTime.UtcNow,
                    rowVersionBase64 = updatedItem.RowVersionBase64
                }
            }
        });
        deleteSyncResponse.EnsureSuccessStatusCode();

        var deletedItemResponse = await client.GetAsync($"/api/v1/inventory/items/{item.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedItemResponse.StatusCode);
    }

    [Fact]
    public async Task CreateSale_WithSameClientRequestId_IsIdempotent()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-idem@shopkeeper.local", "Idempotent Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "IDEMP-1001");
        var requestId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            customerName = "Retry Buyer",
            customerPhone = "08033333333",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            clientRequestId = requestId,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 2150m, reference = (string?)null }
            }
        };

        var first = await client.PostAsJsonAsync("/api/v1/sales/", payload);
        first.EnsureSuccessStatusCode();
        var firstSale = (await first.Content.ReadFromJsonAsync<CreateSaleEnvelope>())!;

        var second = await client.PostAsJsonAsync("/api/v1/sales/", payload);
        second.EnsureSuccessStatusCode();
        var secondSale = (await second.Content.ReadFromJsonAsync<CreateSaleEnvelope>())!;

        Assert.Equal(firstSale.Id, secondSale.Id);
        Assert.Equal(firstSale.SaleNumber, secondSale.SaleNumber);
    }

    [Fact]
    public async Task CustomerReceipt_ReturnsAggregateCashTenderedAndChange()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-cash-receipt@shopkeeper.local", "Cash Receipt Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "CASH-R-1001");
        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Cash Buyer",
            customerPhone = "08035555555",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 2150m, reference = (string?)null, cashTendered = 3000m }
            }
        });
        saleResponse.EnsureSuccessStatusCode();
        var sale = (await saleResponse.Content.ReadFromJsonAsync<CreateSaleEnvelope>())!;

        var receiptResponse = await client.GetAsync($"/api/v1/sales/{sale.Id}/receipt");
        receiptResponse.EnsureSuccessStatusCode();
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<ReceiptEnvelope>())!;

        Assert.Equal(2150m, receipt.TotalCashAmount);
        Assert.Equal(3000m, receipt.TotalCashTendered);
        Assert.Equal(850m, receipt.ChangeDue);
        Assert.Single(receipt.Payments);
        Assert.Equal(3000m, receipt.Payments[0].CashTendered);
        Assert.Equal(850m, receipt.Payments[0].ChangeDue);
    }

    [Fact]
    public async Task CreateSale_RejectsCashTenderedLowerThanCashAmount()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-bad-cash@shopkeeper.local", "Cash Validation Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "CASH-V-1001");
        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Cash Buyer",
            customerPhone = "08036666666",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 2150m, reference = (string?)null, cashTendered = 2000m }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, saleResponse.StatusCode);
    }

    [Fact]
    public async Task CreateSale_RejectsCashTenderedForNonCashPayment()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-bad-noncash@shopkeeper.local", "NonCash Validation Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "NONCASH-V-1001");
        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Transfer Buyer",
            customerPhone = "08037777777",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 2, amount = 2150m, reference = "TRX-1001", cashTendered = 3000m }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, saleResponse.StatusCode);
    }

    [Fact]
    public async Task OwnerReceipt_RestrictsSalesperson_AndAllowsOwnerAndManager()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-owner-receipt@shopkeeper.local", "Owner Receipt Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "OWNER-R-1001");
        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Margin Buyer",
            customerPhone = "08038888888",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 2150m, reference = (string?)null, cashTendered = 2500m }
            }
        });
        saleResponse.EnsureSuccessStatusCode();
        var sale = (await saleResponse.Content.ReadFromJsonAsync<CreateSaleEnvelope>())!;

        var ownerReceiptResponse = await client.GetAsync($"/api/v1/sales/{sale.Id}/receipt/owner");
        ownerReceiptResponse.EnsureSuccessStatusCode();
        var ownerReceipt = (await ownerReceiptResponse.Content.ReadFromJsonAsync<OwnerReceiptEnvelope>())!;
        Assert.Equal(1000m, ownerReceipt.TotalCogs);
        Assert.Equal(1150m, ownerReceipt.GrossProfit);
        Assert.Equal("Test Owner", ownerReceipt.CreatedByName);
        Assert.Single(ownerReceipt.Lines);
        Assert.Equal(1000m, ownerReceipt.Lines[0].CostPrice);
        Assert.Equal(1000m, ownerReceipt.Lines[0].LineProfit);

        var manager = await InviteStaffAndLogin(client, owner.ShopId, "manager-owner-receipt@shopkeeper.local", "ShopManager", "Manager User");
        SetBearer(client, manager.AccessToken);
        var managerReceiptResponse = await client.GetAsync($"/api/v1/sales/{sale.Id}/receipt/owner");
        managerReceiptResponse.EnsureSuccessStatusCode();

        SetBearer(client, owner.AccessToken);
        var salesperson = await InviteStaffAndLogin(client, owner.ShopId, "sales-owner-receipt@shopkeeper.local", "Salesperson", "Sales User");
        SetBearer(client, salesperson.AccessToken);
        var salesReceiptResponse = await client.GetAsync($"/api/v1/sales/{sale.Id}/receipt/owner");
        Assert.Equal(HttpStatusCode.Forbidden, salesReceiptResponse.StatusCode);
    }

    [Fact]
    public async Task AddPayment_RejectsOverpayment()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-overpay@shopkeeper.local", "Overpay Shop");
        SetBearer(client, owner.AccessToken);

        var item = await CreateInventoryItem(client, "OVERPAY-1001");
        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales/", new
        {
            customerName = "Overpay Buyer",
            customerPhone = "08034444444",
            discountAmount = 0m,
            isCredit = false,
            dueDateUtc = (DateTime?)null,
            clientRequestId = Guid.NewGuid().ToString("N"),
            lines = new[]
            {
                new { inventoryItemId = item.Id, quantity = 1, unitPrice = 2000m }
            },
            initialPayments = new[]
            {
                new { method = 1, amount = 1000m, reference = (string?)null }
            }
        });
        saleResponse.EnsureSuccessStatusCode();
        var sale = (await saleResponse.Content.ReadFromJsonAsync<CreateSaleEnvelope>())!;

        var overpayment = await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/payments", new
        {
            method = 1,
            amount = sale.OutstandingAmount + 1,
            reference = (string?)null,
            note = "too much",
            clientRequestId = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.BadRequest, overpayment.StatusCode);
    }

    [Fact]
    public async Task SyncPull_WithCursor_DrainsBacklogWithoutSkippingChanges()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-sync-cursor@shopkeeper.local", "Sync Cursor Shop");
        SetBearer(client, owner.AccessToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopkeeperDbContext>();
            var baseTime = SystemClock.Instance.GetCurrentInstant() - Duration.FromMinutes(10);
            for (var i = 0; i < 501; i++)
            {
                db.SyncChanges.Add(new SyncChange
                {
                    TenantId = owner.ShopId,
                    DeviceId = "server-seed",
                    EntityName = nameof(InventoryItem),
                    EntityId = Guid.NewGuid(),
                    Operation = SyncOperation.Update,
                    PayloadJson = "{}",
                    ClientUpdatedAtUtc = baseTime + Duration.FromSeconds(i),
                    ServerUpdatedAtUtc = baseTime + Duration.FromSeconds(i),
                    Status = SyncStatus.Accepted
                });
            }

            await db.SaveChangesAsync();
        }

        var firstPull = await client.PostAsJsonAsync("/api/v1/sync/pull", new
        {
            deviceId = "tablet-1",
            sinceUtc = DateTime.UtcNow.AddHours(-1),
            cursor = (string?)null
        });
        firstPull.EnsureSuccessStatusCode();
        var firstPage = (await firstPull.Content.ReadFromJsonAsync<SyncPullEnvelope>())!;
        Assert.True(firstPage.HasMore);
        Assert.Equal(500, firstPage.Changes.Count);
        Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        var secondPull = await client.PostAsJsonAsync("/api/v1/sync/pull", new
        {
            deviceId = "tablet-1",
            sinceUtc = firstPage.ServerTimestampUtc,
            cursor = firstPage.NextCursor
        });
        secondPull.EnsureSuccessStatusCode();
        var secondPage = (await secondPull.Content.ReadFromJsonAsync<SyncPullEnvelope>())!;
        Assert.False(secondPage.HasMore);
        Assert.Single(secondPage.Changes);
    }


    [Fact]
    public async Task InventoryList_ConditionalGet_Returns304_AndRefreshesAfterMutation()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-cache-inventory@shopkeeper.local", "Cache Inventory Shop");
        SetBearer(client, owner.AccessToken);
        await CreateInventoryItem(client, "CACHE-INV-1");

        var firstResponse = await client.GetAsync("/api/v1/inventory/items");
        firstResponse.EnsureSuccessStatusCode();
        var firstEtag = firstResponse.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(firstEtag));

        using var notModifiedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/items");
        notModifiedRequest.Headers.IfNoneMatch.ParseAdd(firstEtag);
        var notModifiedResponse = await client.SendAsync(notModifiedRequest);
        Assert.Equal(HttpStatusCode.NotModified, notModifiedResponse.StatusCode);

        await CreateInventoryItem(client, "CACHE-INV-2");

        using var refreshedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/items");
        refreshedRequest.Headers.IfNoneMatch.ParseAdd(firstEtag);
        var refreshedResponse = await client.SendAsync(refreshedRequest);
        refreshedResponse.EnsureSuccessStatusCode();
        Assert.NotEqual(firstEtag, refreshedResponse.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task ShopsMe_ConditionalGet_Returns304_AndRefreshesAfterSettingsUpdate()
    {
        await using var factory = new ShopkeeperApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var owner = await RegisterOwner(client, "owner-cache-shops@shopkeeper.local", "Cache Shops");
        SetBearer(client, owner.AccessToken);

        var firstResponse = await client.GetAsync("/api/v1/shops/me");
        firstResponse.EnsureSuccessStatusCode();
        var firstEtag = firstResponse.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(firstEtag));
        var shops = (await firstResponse.Content.ReadFromJsonAsync<List<ShopEnvelope>>())!;
        var shop = Assert.Single(shops);

        using var notModifiedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/shops/me");
        notModifiedRequest.Headers.IfNoneMatch.ParseAdd(firstEtag);
        var notModifiedResponse = await client.SendAsync(notModifiedRequest);
        Assert.Equal(HttpStatusCode.NotModified, notModifiedResponse.StatusCode);

        var updateResponse = await client.PatchAsJsonAsync($"/api/v1/shops/{shop.Id}/settings", new
        {
            vatEnabled = false,
            vatRate = 0m,
            defaultDiscountPercent = 0.15m,
            rowVersionBase64 = shop.RowVersionBase64
        });
        updateResponse.EnsureSuccessStatusCode();

        using var refreshedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/shops/me");
        refreshedRequest.Headers.IfNoneMatch.ParseAdd(firstEtag);
        var refreshedResponse = await client.SendAsync(refreshedRequest);
        refreshedResponse.EnsureSuccessStatusCode();
        Assert.NotEqual(firstEtag, refreshedResponse.Headers.ETag?.ToString());
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
        return ((await response.Content.ReadFromJsonAsync<AuthEnvelope>())!) with { Email = uniqueEmail };
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

    private static async Task<AuthEnvelope> InviteStaffAndLogin(HttpClient client, Guid shopId, string email, string role, string name)
    {
        var inviteResponse = await client.PostAsJsonAsync($"/api/v1/shops/{shopId}/staff/invite", new
        {
            fullName = name,
            email,
            phone = (string?)null,
            temporaryPassword = "Shopkeeper123!",
            role
        });
        inviteResponse.EnsureSuccessStatusCode();
        var invited = (await inviteResponse.Content.ReadFromJsonAsync<StaffMembershipEnvelope>())!;

        var activateResponse = await client.PostAsync($"/api/v1/shops/{shopId}/staff/{invited.StaffId}/activate", content: null);
        activateResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login = email,
            password = "Shopkeeper123!",
            shopId
        });
        loginResponse.EnsureSuccessStatusCode();
        return (await loginResponse.Content.ReadFromJsonAsync<AuthEnvelope>())!;
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

    private sealed record AuthEnvelope(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc, Guid ShopId, string Role, string? Email = null);
    private sealed record InventoryItemEnvelope(Guid Id, string ProductName, int Quantity, string RowVersionBase64);
    private sealed record ShopEnvelope(Guid Id, string Name, string Code, bool VatEnabled, decimal VatRate, decimal DefaultDiscountPercent, string Role, string RowVersionBase64);
    private sealed record CreateSaleEnvelope(Guid Id, string SaleNumber, decimal TotalAmount, decimal OutstandingAmount);
    private sealed record SalePaymentEnvelope(Guid Id, decimal Amount, string Method, string? Reference);
    private sealed record ReceiptPaymentEnvelope(int Method, decimal Amount, string? Reference, decimal? CashTendered, decimal? ChangeDue);
    private sealed record ReceiptEnvelope(
        Guid SaleId,
        string SaleNumber,
        DateTime CreatedAtUtc,
        string ShopName,
        string? CustomerName,
        decimal Subtotal,
        decimal VatAmount,
        decimal DiscountAmount,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal OutstandingAmount,
        decimal? TotalCashAmount,
        decimal? TotalCashTendered,
        decimal? ChangeDue,
        List<ReceiptPaymentEnvelope> Payments);
    private sealed record OwnerReceiptLineEnvelope(string ProductName, int Quantity, decimal UnitPrice, decimal CostPrice, decimal LineTotal, decimal LineProfit);
    private sealed record OwnerReceiptEnvelope(
        Guid SaleId,
        string SaleNumber,
        DateTime CreatedAtUtc,
        string ShopName,
        string? CustomerName,
        string CreatedByName,
        decimal Subtotal,
        decimal VatAmount,
        decimal DiscountAmount,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal OutstandingAmount,
        decimal? TotalCashAmount,
        decimal? TotalCashTendered,
        decimal? ChangeDue,
        decimal TotalCogs,
        decimal GrossProfit,
        decimal GrossMarginPct,
        List<OwnerReceiptLineEnvelope> Lines,
        List<ReceiptPaymentEnvelope> Payments);
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
    private sealed record StaffMembershipEnvelope(Guid StaffId, Guid UserId, string FullName, string? Email, string? Phone, string Role, bool IsActive, DateTime CreatedAtUtc);
    private sealed record ExpenseCategoryEnvelope(string Category, decimal Amount);
    private sealed record ProfitLossEnvelope(
        DateTime GeneratedAtUtc,
        DateTime FromUtc,
        DateTime ToUtc,
        decimal Revenue,
        decimal Cogs,
        decimal GrossProfit,
        decimal Expenses,
        decimal NetProfitLoss,
        List<ExpenseCategoryEnvelope> ExpenseBreakdown);
    private sealed record ReportFileEnvelope(
        Guid Id,
        string ReportType,
        string Format,
        string FileName,
        string ContentType,
        long ByteLength,
        DateTime CreatedAtUtc);
    private sealed record ReportJobEnvelope(
        Guid Id,
        string ReportType,
        string Format,
        string Status,
        string? FilterJson,
        Guid? ReportFileId,
        DateTime RequestedAtUtc,
        DateTime? CompletedAtUtc,
        string? FailureReason);
    private sealed record SessionEnvelope(
        Guid SessionId,
        Guid ShopId,
        string Role,
        string? DeviceId,
        string? DeviceName,
        DateTime CreatedAtUtc,
        DateTime ExpiresAtUtc,
        DateTime? LastSeenAtUtc,
        bool IsRevoked);
    private sealed record SyncPullEnvelope(DateTime ServerTimestampUtc, List<SyncPushEnvelope> Changes, bool HasMore, string? NextCursor);
    private sealed record SyncPushEnvelope(string DeviceId, string EntityName, Guid EntityId, int Operation, string PayloadJson, DateTime ClientUpdatedAtUtc, string? RowVersionBase64);
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
