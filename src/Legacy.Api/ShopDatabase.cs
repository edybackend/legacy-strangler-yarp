using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Legacy.Api;

/// <summary>
/// The legacy shop's database. During the migration the modern service reads the same file (read-only):
/// a shared database is the usual first step of a strangler migration, and the trade-off is written down in the README.
/// </summary>
public static class ShopDatabase
{
    public static async Task InitializeAsync(string connectionString)
    {
        var file = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (Path.GetDirectoryName(Path.GetFullPath(file)) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS customers (
                id         INTEGER PRIMARY KEY,
                name       TEXT NOT NULL,
                email      TEXT NOT NULL,
                created_on TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS orders (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id INTEGER NOT NULL REFERENCES customers (id),
                status      TEXT NOT NULL,
                placed_at   TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS order_items (
                order_id         INTEGER NOT NULL REFERENCES orders (id),
                line             INTEGER NOT NULL,
                product          TEXT NOT NULL,
                quantity         INTEGER NOT NULL,
                unit_price       TEXT NOT NULL,   -- decimal as text, e.g. "19.99"
                discount_percent INTEGER NOT NULL,
                PRIMARY KEY (order_id, line)
            );
            """);

        if (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM customers") > 0)
        {
            return;
        }

        await using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(
            "INSERT INTO customers (id, name, email, created_on) VALUES (@Id, @Name, @Email, @CreatedOn)",
            new[]
            {
                new { Id = 1, Name = "Ana Souza", Email = "ana@example.com", CreatedOn = "2019-03-14" },
                new { Id = 2, Name = "Bruno Lima", Email = "bruno@example.com", CreatedOn = "2020-07-01" },
                new { Id = 3, Name = "Carla Mendes", Email = "carla@example.com", CreatedOn = "2021-11-23" },
                new { Id = 4, Name = "Diego Rocha", Email = "diego@example.com", CreatedOn = "2023-02-08" },
            },
            transaction);

        // Some of these totals end in a half cent once the discount is applied: that is where the legacy bug shows.
        var orders = new (int Customer, string Status, string PlacedAt, (string Product, int Quantity, string Price, int Discount)[] Items)[]
        {
            (1, "Shipped", "2026-10-02T14:10:00Z", [("Keyboard", 1, "149.90", 0), ("Mouse", 2, "59.90", 0)]),
            (1, "Shipped", "2026-10-19T09:30:00Z", [("Monitor 27\"", 1, "1299.00", 10)]),
            (2, "Delivered", "2026-09-12T18:45:00Z", [("Notebook stand", 3, "19.99", 10)]),
            (2, "Shipped", "2026-11-03T11:00:00Z", [("USB-C cable", 5, "12.45", 5), ("Charger", 1, "89.90", 0)]),
            (3, "Delivered", "2026-08-21T16:20:00Z", [("Headset", 1, "349.90", 15)]),
            (3, "Cancelled", "2026-10-30T08:05:00Z", [("Webcam", 1, "199.00", 0)]),
            (4, "Shipped", "2026-11-10T13:40:00Z", [("Desk lamp", 2, "64.95", 25), ("Mouse pad", 1, "24.90", 0)]),
            (4, "Delivered", "2026-07-07T10:15:00Z", [("Office chair", 1, "899.99", 0)]),
        };
        foreach (var (customer, status, placedAt, items) in orders)
        {
            var id = await connection.ExecuteScalarAsync<long>(
                "INSERT INTO orders (customer_id, status, placed_at) VALUES (@customer, @status, @placedAt); SELECT last_insert_rowid();",
                new { customer, status, placedAt },
                transaction);
            await connection.ExecuteAsync(
                "INSERT INTO order_items (order_id, line, product, quantity, unit_price, discount_percent) VALUES (@id, @line, @Product, @Quantity, @Price, @Discount)",
                items.Select((item, line) => new { id, line = line + 1, item.Product, item.Quantity, item.Price, item.Discount }),
                transaction);
        }

        transaction.Commit();
    }

    public static string Today(TimeProvider clock) => clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
