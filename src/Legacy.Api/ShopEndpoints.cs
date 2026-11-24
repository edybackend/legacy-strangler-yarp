using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Legacy.Api;

/// <summary>
/// The legacy shop: every endpoint in one place, SQL inline, totals computed with <c>double</c> and truncated to
/// cents. It works, it has users, and it is what the gateway is migrating away from, one route at a time.
/// </summary>
public static class ShopEndpoints
{
    public static void MapShop(this WebApplication app, string connectionString, TimeProvider clock)
    {
        app.MapGet("/api/customers", async (string? search) =>
        {
            await using var db = new SqliteConnection(connectionString);
            var rows = await db.QueryAsync<CustomerRow>(
                "SELECT id AS Id, name AS Name, email AS Email, created_on AS CreatedOn FROM customers WHERE @search IS NULL OR name LIKE '%' || @search || '%' ORDER BY id",
                new { search });
            return Results.Ok(rows.Select(ToCustomer));
        });

        app.MapGet("/api/customers/{id:int}", async (int id) =>
        {
            await using var db = new SqliteConnection(connectionString);
            var row = await db.QuerySingleOrDefaultAsync<CustomerRow>(
                "SELECT id AS Id, name AS Name, email AS Email, created_on AS CreatedOn FROM customers WHERE id = @id", new { id });
            return row is null ? NotFound($"Customer {id} not found.") : Results.Ok(ToCustomer(row));
        });

        app.MapGet("/api/orders", async (int? customerId) =>
        {
            await using var db = new SqliteConnection(connectionString);
            var orders = await db.QueryAsync<OrderRow>(
                "SELECT id AS Id, customer_id AS CustomerId, status AS Status, placed_at AS PlacedAt FROM orders WHERE @customerId IS NULL OR customer_id = @customerId ORDER BY id",
                new { customerId });
            var result = new List<object>();
            foreach (var order in orders)
            {
                var items = await ItemsAsync(db, order.Id);
                result.Add(new { order.Id, order.CustomerId, order.Status, order.PlacedAt, Total = Total(items) });
            }

            return Results.Ok(result);
        });

        app.MapGet("/api/orders/{id:int}", async (int id) =>
        {
            await using var db = new SqliteConnection(connectionString);
            var order = await db.QuerySingleOrDefaultAsync<OrderRow>(
                "SELECT id AS Id, customer_id AS CustomerId, status AS Status, placed_at AS PlacedAt FROM orders WHERE id = @id", new { id });
            if (order is null)
            {
                return NotFound($"Order {id} not found.");
            }

            var items = await ItemsAsync(db, id);
            return Results.Ok(new
            {
                order.Id,
                order.CustomerId,
                order.Status,
                order.PlacedAt,
                Items = items.Select(i => new { i.Product, i.Quantity, UnitPrice = decimal.Parse(i.UnitPrice, CultureInfo.InvariantCulture), i.DiscountPercent }),
                Total = Total(items),
            });
        });

        app.MapPost("/api/orders", async (NewOrder request) =>
        {
            if (request.Items is not { Count: > 0 } || request.Items.Any(i => i.Quantity <= 0 || i.UnitPrice <= 0 || i.DiscountPercent is < 0 or > 100 || string.IsNullOrWhiteSpace(i.Product)))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["At least one valid item is required."] });
            }

            await using var db = new SqliteConnection(connectionString);
            await db.OpenAsync();
            if (await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM customers WHERE id = @CustomerId", request) == 0)
            {
                return NotFound($"Customer {request.CustomerId} not found.");
            }

            await using var transaction = db.BeginTransaction();
            var id = await db.ExecuteScalarAsync<long>(
                "INSERT INTO orders (customer_id, status, placed_at) VALUES (@CustomerId, 'Placed', @now); SELECT last_insert_rowid();",
                new { request.CustomerId, now = clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                transaction);
            await db.ExecuteAsync(
                "INSERT INTO order_items (order_id, line, product, quantity, unit_price, discount_percent) VALUES (@id, @line, @Product, @Quantity, @Price, @DiscountPercent)",
                request.Items.Select((i, line) => new { id, line = line + 1, i.Product, i.Quantity, Price = i.UnitPrice.ToString(CultureInfo.InvariantCulture), i.DiscountPercent }),
                transaction);
            transaction.Commit();
            return Results.Created($"/api/orders/{id}", new { Id = id });
        });

        app.MapPost("/api/orders/{id:int}/cancel", async (int id) =>
        {
            await using var db = new SqliteConnection(connectionString);
            var changed = await db.ExecuteAsync("UPDATE orders SET status = 'Cancelled' WHERE id = @id AND status IN ('Placed', 'Shipped')", new { id });
            return changed == 1 ? Results.NoContent() : Results.Problem("Order not found or cannot be cancelled.", statusCode: StatusCodes.Status409Conflict);
        });
    }

    // The legacy rule, kept exactly as it is in production: double arithmetic, then truncate to cents.
    private static decimal Total(IEnumerable<ItemRow> items)
    {
        double total = 0;
        foreach (var i in items)
        {
            total += double.Parse(i.UnitPrice, CultureInfo.InvariantCulture) * i.Quantity * (100 - i.DiscountPercent) / 100;
        }

        return (decimal)(Math.Floor(total * 100) / 100);
    }

    private static async Task<List<ItemRow>> ItemsAsync(SqliteConnection db, long orderId) =>
        [.. await db.QueryAsync<ItemRow>(
            "SELECT product AS Product, quantity AS Quantity, unit_price AS UnitPrice, discount_percent AS DiscountPercent FROM order_items WHERE order_id = @orderId ORDER BY line",
            new { orderId })];

    private static object ToCustomer(CustomerRow c) => new { c.Id, c.Name, c.Email, Since = c.CreatedOn };

    private static IResult NotFound(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status404NotFound);

    public sealed record NewOrder(long CustomerId, List<NewItem>? Items);

    public sealed record NewItem(string Product, int Quantity, decimal UnitPrice, int DiscountPercent);

    private sealed record CustomerRow(long Id, string Name, string Email, string CreatedOn);

    private sealed record OrderRow(long Id, long CustomerId, string Status, string PlacedAt);

    private sealed record ItemRow(string Product, long Quantity, string UnitPrice, long DiscountPercent);
}
