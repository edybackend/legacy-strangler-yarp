using System.Text.Json.Serialization;
using Gateway.Migration;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var routes = builder.Configuration.GetSection("Migration:Routes").Get<Dictionary<string, RouteMigrationOptions>>()
    ?? throw new InvalidOperationException("Migration:Routes is missing.");
var modernAddress = builder.Configuration["ReverseProxy:Clusters:shop:Destinations:modern:Address"]
    ?? throw new InvalidOperationException("The modern destination address is missing.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new MigrationState(routes));
builder.Services.AddSingleton<MigrationStats>();
builder.Services.AddSingleton<ShadowBacklog>();
builder.Services.AddSingleton<MigrationProxyStep>();
builder.Services.AddHostedService<ShadowComparer>();
builder.Services.AddHttpClient(ShadowComparer.ModernClient, c =>
{
    c.BaseAddress = new Uri(modernAddress);
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("Strangler gateway"));
app.MapGet("/health", () => Results.Ok("gateway"));
app.MapMigrationEndpoints();
var step = app.Services.GetRequiredService<MigrationProxyStep>();
app.MapReverseProxy(pipeline =>
{
    pipeline.Use(step.InvokeAsync);
    pipeline.UseLoadBalancing();
});

await app.RunAsync();

namespace Gateway
{
    /// <summary>Marker for WebApplicationFactory (each service has its own <c>Program</c>).</summary>
    public sealed class GatewayMarker;
}
