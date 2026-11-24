using Modern.Api;
using Modern.Api.Customers;
using Modern.Api.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Shop") ?? throw new InvalidOperationException("Connection string 'Shop' is missing.");
builder.Services.AddSingleton(new ShopReadDatabase(connectionString));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("Modern shop service"));
app.MapGet("/health", () => Results.Ok("modern"));
app.MapCustomers();
app.MapOrders();

await app.RunAsync();

namespace Modern.Api
{
    /// <summary>Marker for WebApplicationFactory (each service has its own <c>Program</c>).</summary>
    public sealed class ModernApiMarker;
}
