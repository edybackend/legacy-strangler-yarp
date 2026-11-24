using Modern.Api.Orders;

namespace Strangler.UnitTests;

public class OrderTotalsTests
{
    [Theory]
    [InlineData(1, "1299.00", 10, "1169.10")] // double: 1169.0999… → legacy truncates to 1169.09
    [InlineData(3, "19.99", 10, "53.97")]     // 53.973 → 53.97 (both services agree)
    [InlineData(2, "64.95", 25, "97.43")]     // 97.425 → half away from zero
    [InlineData(1, "899.99", 0, "899.99")]
    public void A_line_is_rounded_to_the_cent_half_away_from_zero(int quantity, string price, int discount, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            OrderTotals.LineTotal(new OrderLine("x", quantity, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), discount)));

    [Fact]
    public void The_total_is_the_sum_of_rounded_lines() =>
        Assert.Equal(122.33m, OrderTotals.Total([new OrderLine("Desk lamp", 2, 64.95m, 25), new OrderLine("Mouse pad", 1, 24.90m, 0)]));
}
