using System.Text.Json.Nodes;
using Gateway.Migration;

namespace Strangler.IntegrationTests;

/// <summary>
/// Contract test between the two services, outside the gateway: same URLs, same JSON. It documents the only
/// known difference, so a new one fails the build instead of surprising users after the switch.
/// </summary>
public sealed class ParityTests : IDisposable
{
    private readonly ShopEnvironment _shop = new();

    private static CancellationToken Ct => ShopEnvironment.Ct;

    [Theory]
    [InlineData("/api/customers")]
    [InlineData("/api/customers/1")]
    [InlineData("/api/customers?search=Lima")]
    [InlineData("/api/orders/1")]
    [InlineData("/api/orders/3")]
    [InlineData("/api/orders/6")]
    [InlineData("/api/orders/8")]
    public async Task The_modern_service_answers_like_the_legacy_one(string path) =>
        Assert.Empty(await DifferencesAsync(path));

    [Theory]
    [InlineData("/api/orders/2", "$.total", "1169.09", "1169.10")]
    [InlineData("/api/orders/4", "$.total", "149.03", "149.04")]
    [InlineData("/api/orders/5", "$.total", "297.41", "297.42")]
    [InlineData("/api/orders/7", "$.total", "122.32", "122.33")]
    [InlineData("/api/orders?customerId=3", "$[0].total", "297.41", "297.42")] // order 6 of the same customer matches
    public async Task The_only_known_difference_is_the_legacy_rounding_bug(string path, string field, string legacy, string modern) =>
        Assert.Equal([new Difference(field, legacy, modern)], await DifferencesAsync(path));

    public void Dispose() => _shop.Dispose();

    private async Task<IReadOnlyList<Difference>> DifferencesAsync(string path) =>
        JsonDiff.Compare(
            JsonNode.Parse(await _shop.Legacy.GetStringAsync(path, Ct)),
            JsonNode.Parse(await _shop.Modern.GetStringAsync(path, Ct)));
}
