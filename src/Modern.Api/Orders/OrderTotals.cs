namespace Modern.Api.Orders;

public sealed record OrderLine(string Product, int Quantity, decimal UnitPrice, int DiscountPercent);

/// <summary>
/// Order total in <c>decimal</c>, each line rounded to the cent (half away from zero), then summed.
/// The legacy service computes it in <c>double</c> and truncates, which loses a cent on some orders;
/// the gateway's shadow mode is what found that difference.
/// </summary>
public static class OrderTotals
{
    public static decimal LineTotal(OrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Math.Round(line.UnitPrice * line.Quantity * (100 - line.DiscountPercent) / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal Total(IEnumerable<OrderLine> lines) => lines.Sum(LineTotal);
}
