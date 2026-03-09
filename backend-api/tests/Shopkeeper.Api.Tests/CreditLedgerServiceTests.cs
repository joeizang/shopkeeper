using NodaTime;
using Shopkeeper.Api.Domain;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Tests;

public sealed class CreditLedgerServiceTests
{
    [Fact]
    public void ApplyRepayment_WhenOutstandingHitsZero_MarksSettled()
    {
        var service = new CreditLedgerService();
        var account = new CreditAccount
        {
            TenantId = Guid.NewGuid(),
            SaleId = Guid.NewGuid(),
            DueDateUtc = SystemClock.Instance.GetCurrentInstant() + Duration.FromDays(7),
            OutstandingAmount = 2000m,
            Status = CreditStatus.Open
        };

        service.ApplyRepayment(account, 2000m);

        Assert.Equal(0m, account.OutstandingAmount);
        Assert.Equal(CreditStatus.Settled, account.Status);
    }
}
