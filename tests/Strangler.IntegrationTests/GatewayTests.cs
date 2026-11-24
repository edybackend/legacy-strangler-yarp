using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Strangler.IntegrationTests;

public sealed class GatewayTests : IDisposable
{
    private readonly ShopEnvironment _shop = new();

    private static CancellationToken Ct => ShopEnvironment.Ct;

    [Fact]
    public async Task A_migrated_route_is_served_by_the_modern_service_with_the_same_answer()
    {
        using var response = await _shop.Gateway.GetAsync("/api/customers/2", Ct);

        Assert.Equal("modern", response.Headers.GetValues("X-Served-By").Single());
        Assert.Equal(await _shop.Legacy.GetStringAsync("/api/customers/2", Ct), await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Shadow_serves_legacy_and_reports_exactly_the_orders_hit_by_the_rounding_bug()
    {
        for (var id = 1; id <= 8; id++)
        {
            Assert.Equal("legacy", await _shop.ServedByAsync($"/api/orders/{id}"));
        }

        await Eventually(async () => (await _shop.RouteAsync("orders-read")).Counters.ShadowCompared == 8);
        var mismatches = (await _shop.Gateway.GetFromJsonAsync<List<MismatchDto>>("/migration/api/mismatches?routeId=orders-read", ShopEnvironment.Json, Ct))!;

        Assert.Equal(["/api/orders/2", "/api/orders/4", "/api/orders/5", "/api/orders/7"], mismatches.Select(m => m.Path).Order());
        Assert.All(mismatches, m => Assert.Equal("$.total", Assert.Single(m.Differences).Path));
        Assert.Contains(mismatches, m => m.Differences[0] == new DifferenceDto("$.total", "1169.09", "1169.10"));
    }

    [Fact]
    public async Task When_the_modern_service_is_down_shadow_clients_are_not_affected()
    {
        _shop.Router.ModernIsDown = true;

        using var response = await _shop.Gateway.GetAsync("/api/orders/1", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Eventually(async () => (await _shop.RouteAsync("orders-read")).Counters.ShadowErrors == 1);
        Assert.Equal(0, (await _shop.RouteAsync("orders-read")).Counters.ShadowMismatches);
    }

    [Fact]
    public async Task Canary_sends_the_configured_share_and_sticks_per_key()
    {
        await _shop.SetModeAsync("orders-read", "Canary", 0);
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal("legacy", await _shop.ServedByAsync("/api/orders/1"));
        }

        await _shop.SetModeAsync("orders-read", "Canary", 100);
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal("modern", await _shop.ServedByAsync("/api/orders/1"));
        }

        await _shop.SetModeAsync("orders-read", "Canary", 30);
        var served = new List<string>();
        for (var i = 0; i < 300; i++)
        {
            served.Add(await _shop.ServedByAsync("/api/orders/1", canaryKey: $"customer-{i}"));
        }

        Assert.InRange(served.Count(s => s == "modern"), 60, 120); // 30 % of 300 = 90
        var first = await _shop.ServedByAsync("/api/orders/1", "customer-7");
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(first, await _shop.ServedByAsync("/api/orders/1", "customer-7"));
        }
    }

    [Fact]
    public async Task Unsafe_changes_are_refused_and_rollback_is_one_call()
    {
        async Task<HttpStatusCode> Put(string route, object body) =>
            (await _shop.Gateway.PutAsJsonAsync($"/migration/api/routes/{route}", body, Ct)).StatusCode;

        Assert.Equal(HttpStatusCode.Conflict, await Put("orders-write", new { mode = "Shadow", canaryPercent = 0 }));
        Assert.Equal(HttpStatusCode.Conflict, await Put("orders-write", new { mode = "Modern", canaryPercent = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest, await Put("orders-read", new { mode = "Canary", canaryPercent = 150 }));
        Assert.Equal(HttpStatusCode.NotFound, await Put("payments", new { mode = "Legacy", canaryPercent = 0 }));

        Assert.Equal(HttpStatusCode.OK, await Put("customers", new { mode = "Legacy", canaryPercent = 0 }));
        Assert.Equal("legacy", await _shop.ServedByAsync("/api/customers/1"));
    }

    [Fact]
    public async Task A_write_through_the_legacy_route_is_visible_through_the_modern_read()
    {
        await _shop.SetModeAsync("orders-read", "Modern");

        using var created = await _shop.Gateway.PostAsJsonAsync(
            "/api/orders", new { customerId = 3, items = new[] { new { product = "Pen", quantity = 3, unitPrice = 2.50m, discountPercent = 0 } } }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("legacy", created.Headers.GetValues("X-Served-By").Single());
        using var read = await _shop.Gateway.GetAsync(created.Headers.Location, Ct);
        Assert.Equal("modern", read.Headers.GetValues("X-Served-By").Single());
        Assert.Contains("\"total\":7.50", await read.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_routes_are_404_and_malformed_json_is_400()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.Gateway.GetAsync("/api/payments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.Gateway.GetAsync("/api/customers/999", Ct)).StatusCode);
        using var malformed = await _shop.Gateway.PostAsync("/api/orders", new StringContent("{bad", Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    public void Dispose() => _shop.Dispose();

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the shadow comparisons.");
            await Task.Delay(30, Ct);
        }
    }
}
