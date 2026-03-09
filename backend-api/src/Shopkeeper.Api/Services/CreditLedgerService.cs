using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Services;

public sealed class CreditLedgerService
{
    public void ApplyRepayment(CreditAccount creditAccount, decimal amount)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("Repayment amount must be greater than zero.");
        }

        if (amount > creditAccount.OutstandingAmount)
        {
            throw new InvalidOperationException("Repayment amount cannot exceed the outstanding balance.");
        }

        creditAccount.OutstandingAmount -= amount;
        creditAccount.Status = creditAccount.OutstandingAmount == 0
            ? CreditStatus.Settled
            : CreditStatus.Open;
    }
}
