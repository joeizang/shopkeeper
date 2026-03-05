using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Data;

public static class DevelopmentSeeder
{
    public static async Task SeedAsync(
        ShopkeeperDbContext db,
        UserManager<UserAccount> userManager,
        CancellationToken ct = default)
    {
        var hasUsers = await db.Users.AnyAsync(ct);
        if (hasUsers)
        {
            return;
        }

        var owner = new UserAccount
        {
            FullName = "Demo Owner",
            Email = "owner@shopkeeper.local",
            UserName = "owner@shopkeeper.local",
            EmailConfirmed = true
        };

        var ownerCreate = await userManager.CreateAsync(owner, "Shopkeeper123!");
        if (!ownerCreate.Succeeded)
        {
            return;
        }

        var shop = new Shop
        {
            Name = "Demo Shop",
            Code = "DEMOSH1234",
            VatEnabled = true,
            VatRate = 0.075m
        };

        var membership = new ShopMembership
        {
            Shop = shop,
            UserAccount = owner,
            Role = MembershipRole.Owner,
            IsActive = true
        };

        var sampleItems = new List<InventoryItem>
        {
            new()
            {
                TenantId = shop.Id,
                ProductName = "Used iPhone 12",
                ModelNumber = "A2403",
                SerialNumber = "DMOIPH12-001",
                Quantity = 3,
                CostPrice = 180000,
                SellingPrice = 230000,
                ItemType = ItemType.Used,
                ConditionGrade = ItemConditionGrade.B,
                ConditionNotes = "Minor scratches"
            },
            new()
            {
                TenantId = shop.Id,
                ProductName = "USB-C Charger",
                ModelNumber = "USBC-20W",
                SerialNumber = "DMOUSBC-001",
                Quantity = 15,
                CostPrice = 3500,
                SellingPrice = 5000,
                ItemType = ItemType.New,
                ConditionGrade = null,
                ConditionNotes = null
            }
        };

        db.Shops.Add(shop);
        db.ShopMemberships.Add(membership);
        db.InventoryItems.AddRange(sampleItems);

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = shop.Id,
            UserAccountId = owner.Id,
            Action = "seed.initialized",
            EntityName = "Shop",
            EntityId = shop.Id,
            PayloadJson = "{\"seed\":\"development\"}"
        });

        await db.SaveChangesAsync(ct);
    }
}
