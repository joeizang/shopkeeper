using Shopkeeper.Api.Contracts;
using Shopkeeper.Api.Services;

namespace Shopkeeper.Api.Tests;

public sealed class SaleCalculatorTests
{
    [Fact]
    public void Calculate_WhenVatEnabled_ComputesTotals()
    {
        var calculator = new SaleCalculator();
        var lines = new List<SaleLineRequest>
        {
            new(Guid.NewGuid(), 2, 5000m),
            new(Guid.NewGuid(), 1, 2500m)
        };

        var totals = calculator.Calculate(lines, 1000m, vatEnabled: true, vatRate: 0.075m);

        Assert.Equal(12500m, totals.Subtotal);
        Assert.Equal(937.5m, totals.VatAmount);
        Assert.Equal(1000m, totals.DiscountAmount);
        Assert.Equal(12437.5m, totals.TotalAmount);
    }
}
