using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Data;

public sealed record E2ESeedResult(
    string ShopId,
    string ShopCode,
    string OwnerEmail,
    string ManagerEmail,
    string SalespersonEmail,
    string Password,
    string InventoryProductName,
    string CreditSaleId,
    string CreditSaleNumber);

public sealed class E2ETestSeeder(ShopkeeperDbContext db, UserManager<UserAccount> userManager)
{
    private const string DefaultPassword = "Shopkeeper123!";

    public async Task<E2ESeedResult> ResetAndSeedAsync(CancellationToken ct = default)
    {
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.MigrateAsync(ct);
        return await SeedAsync(ct);
    }

    public async Task<E2ESeedResult> SeedAsync(CancellationToken ct = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var owner = await CreateUserAsync("owner.e2e@shopkeeper.local", "E2E Owner", ct);
        var manager = await CreateUserAsync("manager.e2e@shopkeeper.local", "E2E Manager", ct);
        var salesperson = await CreateUserAsync("sales.e2e@shopkeeper.local", "E2E Salesperson", ct);

        var shop = new Shop
        {
            Name = "E2E Demo Shop",
            Code = "E2EDEMO1",
            VatEnabled = true,
            VatRate = 0.075m,
            DefaultDiscountPercent = 0.10m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var ownerMembership = new ShopMembership
        {
            Shop = shop,
            UserAccount = owner,
            Role = MembershipRole.Owner,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var managerMembership = new ShopMembership
        {
            Shop = shop,
            UserAccount = manager,
            Role = MembershipRole.ShopManager,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var salesMembership = new ShopMembership
        {
            Shop = shop,
            UserAccount = salesperson,
            Role = MembershipRole.Salesperson,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var laptop = new InventoryItem
        {
            TenantId = shop.Id,
            ProductName = "E2E Laptop",
            ModelNumber = "E2E-LT-15",
            SerialNumber = "E2E-LT-001",
            Quantity = 8,
            CostPrice = 240000m,
            SellingPrice = 300000m,
            ItemType = ItemType.New,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var phone = new InventoryItem
        {
            TenantId = shop.Id,
            ProductName = "E2E Used Phone",
            ModelNumber = "E2E-PH-12",
            SerialNumber = "E2E-PH-001",
            Quantity = 4,
            CostPrice = 120000m,
            SellingPrice = 165000m,
            ItemType = ItemType.Used,
            ConditionGrade = ItemConditionGrade.B,
            ConditionNotes = "Minor wear",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var accessories = new InventoryItem
        {
            TenantId = shop.Id,
            ProductName = "E2E Charger",
            ModelNumber = "E2E-CHR-20W",
            SerialNumber = "E2E-CHR-001",
            Quantity = 20,
            CostPrice = 3500m,
            SellingPrice = 5500m,
            ItemType = ItemType.New,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var completedSale = new Sale
        {
            TenantId = shop.Id,
            SaleNumber = "SALE-0001",
            CustomerName = "Walk-in Buyer",
            Subtotal = 5500m,
            VatAmount = 412.5m,
            DiscountAmount = 0m,
            TotalAmount = 5912.5m,
            OutstandingAmount = 0m,
            IsCredit = false,
            Status = SaleStatus.Completed,
            CreatedByMembershipId = salesMembership.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var completedSaleLine = new SaleLine
        {
            TenantId = shop.Id,
            SaleId = completedSale.Id,
            InventoryItemId = accessories.Id,
            ProductNameSnapshot = accessories.ProductName,
            Quantity = 1,
            UnitPrice = accessories.SellingPrice,
            CostPriceSnapshot = accessories.CostPrice,
            LineTotal = 5500m
        };

        var completedSalePayment = new SalePayment
        {
            TenantId = shop.Id,
            SaleId = completedSale.Id,
            Method = PaymentMethod.Cash,
            Amount = 5912.5m,
            CreatedAtUtc = now
        };

        var creditSale = new Sale
        {
            TenantId = shop.Id,
            SaleNumber = "SALE-0002",
            CustomerName = "Credit Customer",
            CustomerPhone = "08030000000",
            Subtotal = 165000m,
            VatAmount = 12375m,
            DiscountAmount = 16500m,
            TotalAmount = 160875m,
            OutstandingAmount = 120875m,
            IsCredit = true,
            DueDateUtc = now.Plus(Duration.FromDays(14)),
            Status = SaleStatus.Completed,
            CreatedByMembershipId = salesMembership.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var creditSaleLine = new SaleLine
        {
            TenantId = shop.Id,
            SaleId = creditSale.Id,
            InventoryItemId = phone.Id,
            ProductNameSnapshot = phone.ProductName,
            Quantity = 1,
            UnitPrice = phone.SellingPrice,
            CostPriceSnapshot = phone.CostPrice,
            LineTotal = phone.SellingPrice
        };

        var initialCreditPayment = new SalePayment
        {
            TenantId = shop.Id,
            SaleId = creditSale.Id,
            Method = PaymentMethod.Cash,
            Amount = 30000m,
            CreatedAtUtc = now
        };

        var repaymentPayment = new SalePayment
        {
            TenantId = shop.Id,
            SaleId = creditSale.Id,
            Method = PaymentMethod.BankTransfer,
            Amount = 10000m,
            Reference = "E2E-TRX-001",
            CreatedAtUtc = now
        };

        var creditAccount = new CreditAccount
        {
            TenantId = shop.Id,
            SaleId = creditSale.Id,
            DueDateUtc = now.Plus(Duration.FromDays(14)),
            OutstandingAmount = 120875m,
            Status = CreditStatus.Open,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var repayment = new CreditRepayment
        {
            TenantId = shop.Id,
            CreditAccountId = creditAccount.Id,
            SalePaymentId = repaymentPayment.Id,
            Amount = 10000m,
            Notes = "Initial installment",
            CreatedAtUtc = now
        };

        var expense = new Expense
        {
            TenantId = shop.Id,
            Title = "Internet Subscription",
            Category = "Operations",
            Amount = 12000m,
            ExpenseDateUtc = now,
            Notes = "Monthly connectivity",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Shops.Add(shop);
        db.ShopMemberships.AddRange(ownerMembership, managerMembership, salesMembership);
        db.TenantSaleCounters.Add(new TenantSaleCounter { TenantId = shop.Id, NextSaleNumber = 3 });
        db.InventoryItems.AddRange(laptop, phone, accessories);
        db.Sales.AddRange(completedSale, creditSale);
        db.SaleLines.AddRange(completedSaleLine, creditSaleLine);
        db.SalePayments.AddRange(completedSalePayment, initialCreditPayment, repaymentPayment);
        db.CreditAccounts.Add(creditAccount);
        db.CreditRepayments.Add(repayment);
        db.Expenses.Add(expense);

        await db.SaveChangesAsync(ct);

        return new E2ESeedResult(
            ShopId: shop.Id.ToString(),
            ShopCode: shop.Code,
            OwnerEmail: owner.Email!,
            ManagerEmail: manager.Email!,
            SalespersonEmail: salesperson.Email!,
            Password: DefaultPassword,
            InventoryProductName: laptop.ProductName,
            CreditSaleId: creditSale.Id.ToString(),
            CreditSaleNumber: creditSale.SaleNumber);
    }

    private async Task<UserAccount> CreateUserAsync(string email, string fullName, CancellationToken ct)
    {
        var user = new UserAccount
        {
            FullName = fullName,
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            PreferredLanguage = "en",
            Timezone = "Africa/Lagos"
        };

        var result = await userManager.CreateAsync(user, DefaultPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Failed to create E2E user {email}: {string.Join(", ", result.Errors.Select(x => x.Description))}");
        }

        return user;
    }
}
