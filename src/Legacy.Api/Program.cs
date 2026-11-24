using Legacy.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
var app = builder.Build();

var connectionString = app.Configuration.GetConnectionString("Shop") ?? throw new InvalidOperationException("Connection string 'Shop' is missing.");
await ShopDatabase.InitializeAsync(connectionString);

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.MapGet("/health", () => Results.Ok("legacy"));
app.MapShop(connectionString, TimeProvider.System);

await app.RunAsync();

namespace Legacy.Api
{
    /// <summary>Marker for WebApplicationFactory (each service has its own <c>Program</c>).</summary>
    public sealed class LegacyApiMarker;
}
