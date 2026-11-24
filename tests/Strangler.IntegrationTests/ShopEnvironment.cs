using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gateway;
using Legacy.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modern.Api;
using Yarp.ReverseProxy.Forwarder;

namespace Strangler.IntegrationTests;

/// <summary>
/// The three services in test servers. The gateway's real YARP pipeline forwards to <c>http://legacy</c> and
/// <c>http://modern</c>; a routing handler delivers those requests to the matching test server.
/// </summary>
public sealed class ShopEnvironment : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"strangler-tests-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<LegacyApiMarker> _legacy;
    private readonly WebApplicationFactory<ModernApiMarker> _modern;
    private readonly WebApplicationFactory<GatewayMarker> _gateway;

    public ShopEnvironment()
    {
        var connectionString = $"Data Source={_databasePath};Pooling=False";
        _legacy = new WebApplicationFactory<LegacyApiMarker>().WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Shop", connectionString));
        Legacy = _legacy.CreateClient(); // starts the legacy service, which creates and seeds the database
        _modern = new WebApplicationFactory<ModernApiMarker>().WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Shop", connectionString));
        Modern = _modern.CreateClient();

        Router = new BackendRouter(_legacy.Server, _modern.Server);
        _gateway = new WebApplicationFactory<GatewayMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ReverseProxy:Clusters:shop:Destinations:legacy:Address", "http://legacy/");
            b.UseSetting("ReverseProxy:Clusters:shop:Destinations:modern:Address", "http://modern/");
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<IForwarderHttpClientFactory>(new RoutedForwarderFactory(Router));
                services.ConfigureHttpClientDefaults(c => c.ConfigurePrimaryHttpMessageHandler(() => new RoutedHandler(Router)));
            });
        });
        Gateway = _gateway.CreateClient();
    }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public HttpClient Legacy { get; }

    public HttpClient Modern { get; }

    public HttpClient Gateway { get; }

    public BackendRouter Router { get; }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task SetModeAsync(string routeId, string mode, int canaryPercent = 0)
    {
        var response = await Gateway.PutAsJsonAsync($"/migration/api/routes/{routeId}", new { mode, canaryPercent }, Ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<RouteStatusDto> RouteAsync(string routeId) =>
        (await Gateway.GetFromJsonAsync<List<RouteStatusDto>>("/migration/api/routes", Json, Ct))!.Single(r => r.RouteId == routeId);

    public async Task<string> ServedByAsync(string path, string? canaryKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (canaryKey is not null)
        {
            request.Headers.Add("X-Canary-Key", canaryKey);
        }

        using var response = await Gateway.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("X-Served-By").Single();
    }

    public void Dispose()
    {
        Gateway.Dispose();
        Legacy.Dispose();
        Modern.Dispose();
        _gateway.Dispose();
        Router.Dispose();
        _modern.Dispose();
        _legacy.Dispose();
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            File.Delete(file);
        }
    }
}

/// <summary>Sends a request to the test server of its host (legacy or modern). The modern one can be switched off.</summary>
public sealed class BackendRouter(TestServer legacy, TestServer modern) : IDisposable
{
    private readonly HttpMessageInvoker _legacy = new(legacy.CreateHandler());
    private readonly HttpMessageInvoker _modern = new(modern.CreateHandler());

    public bool ModernIsDown { get; set; }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => request.RequestUri!.Host switch
    {
        "legacy" => _legacy.SendAsync(request, cancellationToken),
        "modern" when ModernIsDown => throw new HttpRequestException("Connection refused (modern is down in this test)."),
        "modern" => _modern.SendAsync(request, cancellationToken),
        var host => throw new InvalidOperationException($"No test backend for host '{host}'."),
    };

    public void Dispose()
    {
        _legacy.Dispose();
        _modern.Dispose();
    }
}

public sealed class RoutedHandler(BackendRouter router) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        router.SendAsync(request, cancellationToken);
}

public sealed class RoutedForwarderFactory(BackendRouter router) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new RoutedHandler(router), disposeHandler: true);
}

public sealed record CountersDto(long ServedByLegacy, long ServedByModern, long ShadowCompared, long ShadowMismatches, long ShadowErrors);

public sealed record RouteStatusDto(string RouteId, string Mode, int CanaryPercent, CountersDto Counters);

public sealed record DifferenceDto(string Path, string? Legacy, string? Modern);

public sealed record MismatchDto(string RouteId, string Path, IReadOnlyList<DifferenceDto> Differences);
