using Shopkeeper.Api.Contracts;

namespace Shopkeeper.Api.Services;

public sealed class SaleCalculator
{
    public SaleTotals Calculate(IReadOnlyCollection<SaleLineRequest> lines, decimal discountAmount, bool vatEnabled, decimal vatRate)
    {
        var subtotal = lines.Sum(line => line.UnitPrice * line.Quantity);
        var vatAmount = vatEnabled ? Math.Round(subtotal * vatRate, 2, MidpointRounding.AwayFromZero) : 0m;
        var total = Math.Max(0, subtotal + vatAmount - discountAmount);

        return new SaleTotals(subtotal, vatAmount, discountAmount, total);
    }
}

public readonly record struct SaleTotals(decimal Subtotal, decimal VatAmount, decimal DiscountAmount, decimal TotalAmount);
