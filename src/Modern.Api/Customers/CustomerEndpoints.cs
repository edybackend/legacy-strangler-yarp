using Dapper;

namespace Modern.Api.Customers;

public sealed record Customer(long Id, string Name, string Email, string Since);

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomers(this IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/api/customers").WithTags("Customers");

        customers.MapGet("/", async (string? search, ShopReadDatabase database, CancellationToken ct) =>
        {
            await using var db = await database.OpenAsync(ct);
            return TypedResults.Ok(await db.QueryAsync<Customer>(new CommandDefinition(
                $"{Select} WHERE @search IS NULL OR name LIKE '%' || @search || '%' ORDER BY id", new { search }, cancellationToken: ct)));
        });

        customers.MapGet("/{id:long}", async Task<IResult> (long id, ShopReadDatabase database, CancellationToken ct) =>
        {
            await using var db = await database.OpenAsync(ct);
            var customer = await db.QuerySingleOrDefaultAsync<Customer>(new CommandDefinition($"{Select} WHERE id = @id", new { id }, cancellationToken: ct));
            return customer is null
                ? TypedResults.Problem($"Customer {id} not found.", statusCode: StatusCodes.Status404NotFound)
                : TypedResults.Ok(customer);
        });

        return app;
    }

    private const string Select = "SELECT id AS Id, name AS Name, email AS Email, created_on AS Since FROM customers";
}
