using Dapper;
using Microsoft.Data.Sqlite;

namespace Modern.Api.Orders;

public sealed record OrderSummary(long Id, long CustomerId, string Status, string PlacedAt, decimal Total);

public sealed record OrderDetail(long Id, long CustomerId, string Status, string PlacedAt, IReadOnlyList<OrderLine> Items, decimal Total);

/// <summary>Read side of orders. Same public contract as the legacy service: the clients must not notice the switch.</summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrders(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapGet("/", async (long? customerId, ShopReadDatabase database, CancellationToken ct) =>
        {
            await using var db = await database.OpenAsync(ct);
            var rows = await db.QueryAsync<OrderRow>(new CommandDefinition(
                $"{SelectOrders} WHERE @customerId IS NULL OR customer_id = @customerId ORDER BY id", new { customerId }, cancellationToken: ct));
            var lines = (await db.QueryAsync<LineRow>(new CommandDefinition(
                $"{SelectLines} WHERE @customerId IS NULL OR order_id IN (SELECT id FROM orders WHERE customer_id = @customerId)",
                new { customerId },
                cancellationToken: ct))).ToLookup(l => l.OrderId, l => l.ToLine());
            return TypedResults.Ok(rows.Select(o => new OrderSummary(o.Id, o.CustomerId, o.Status, o.PlacedAt, OrderTotals.Total(lines[o.Id]))));
        });

        orders.MapGet("/{id:long}", async Task<IResult> (long id, ShopReadDatabase database, CancellationToken ct) =>
        {
            await using var db = await database.OpenAsync(ct);
            var order = await db.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition($"{SelectOrders} WHERE id = @id", new { id }, cancellationToken: ct));
            if (order is null)
            {
                return TypedResults.Problem($"Order {id} not found.", statusCode: StatusCodes.Status404NotFound);
            }

            var lines = (await db.QueryAsync<LineRow>(new CommandDefinition($"{SelectLines} WHERE order_id = @id ORDER BY line", new { id }, cancellationToken: ct)))
                .Select(l => l.ToLine())
                .ToList();
            return TypedResults.Ok(new OrderDetail(order.Id, order.CustomerId, order.Status, order.PlacedAt, lines, OrderTotals.Total(lines)));
        });

        return app;
    }

    private const string SelectOrders = "SELECT id AS Id, customer_id AS CustomerId, status AS Status, placed_at AS PlacedAt FROM orders";

    private const string SelectLines =
        "SELECT order_id AS OrderId, product AS Product, quantity AS Quantity, unit_price AS UnitPrice, discount_percent AS DiscountPercent FROM order_items";

    private sealed record OrderRow(long Id, long CustomerId, string Status, string PlacedAt);

    private sealed record LineRow(long OrderId, string Product, long Quantity, string UnitPrice, long DiscountPercent)
    {
        public OrderLine ToLine() =>
            new(Product, (int)Quantity, decimal.Parse(UnitPrice, System.Globalization.CultureInfo.InvariantCulture), (int)DiscountPercent);
    }
}
