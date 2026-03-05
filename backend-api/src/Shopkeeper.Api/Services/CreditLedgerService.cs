using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Services;

public sealed class CreditLedgerService
{
    public void ApplyRepayment(CreditAccount creditAccount, decimal amount)
    {
        creditAccount.OutstandingAmount = Math.Max(0, creditAccount.OutstandingAmount - amount);
        creditAccount.Status = creditAccount.OutstandingAmount == 0
            ? CreditStatus.Settled
            : CreditStatus.Open;
    }
}
