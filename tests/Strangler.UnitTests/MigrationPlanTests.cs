using Gateway.Migration;

namespace Strangler.UnitTests;

public class MigrationPlanTests
{
    private static MigrationState Plan() => new(new Dictionary<string, RouteMigrationOptions>
    {
        ["orders-read"] = new() { Mode = MigrationMode.Legacy, ModernReady = true, ReadOnly = true },
        ["orders-write"] = new() { Mode = MigrationMode.Legacy, ModernReady = false, ReadOnly = false },
        ["payments-write"] = new() { Mode = MigrationMode.Legacy, ModernReady = true, ReadOnly = false },
    });

    [Theory]
    [InlineData("orders-read", MigrationMode.Shadow, 0, ChangeResult.Changed)]
    [InlineData("orders-read", MigrationMode.Canary, 30, ChangeResult.Changed)]
    [InlineData("orders-read", MigrationMode.Canary, 101, ChangeResult.InvalidPercent)]
    [InlineData("orders-write", MigrationMode.Modern, 0, ChangeResult.ModernNotReady)]
    [InlineData("orders-write", MigrationMode.Legacy, 0, ChangeResult.Changed)]
    [InlineData("payments-write", MigrationMode.Shadow, 0, ChangeResult.WritesCannotBeShadowed)]
    [InlineData("payments-write", MigrationMode.Canary, 10, ChangeResult.Changed)]
    [InlineData("nope", MigrationMode.Legacy, 0, ChangeResult.UnknownRoute)]
    public void Changes_follow_the_safety_rules(string route, MigrationMode mode, int percent, ChangeResult expected) =>
        Assert.Equal(expected, Plan().Change(route, mode, percent));

    [Fact]
    public void An_unsafe_initial_configuration_does_not_start() =>
        Assert.Throws<InvalidOperationException>(() => new MigrationState(new Dictionary<string, RouteMigrationOptions>
        {
            ["orders-write"] = new() { Mode = MigrationMode.Shadow, ModernReady = true, ReadOnly = false },
        }));

    [Fact]
    public void Modes_decide_the_destination()
    {
        static RouteMigration Route(MigrationMode mode, int percent = 0) => new("r", mode, percent, true, true);

        Assert.Equal(DestinationSelector.Legacy, DestinationSelector.Choose(Route(MigrationMode.Legacy), null, () => 0));
        Assert.Equal(DestinationSelector.Legacy, DestinationSelector.Choose(Route(MigrationMode.Shadow), null, () => 0));
        Assert.Equal(DestinationSelector.Modern, DestinationSelector.Choose(Route(MigrationMode.Modern), null, () => 99));
        Assert.Equal(DestinationSelector.Modern, DestinationSelector.Choose(Route(MigrationMode.Canary, 30), null, () => 29));
        Assert.Equal(DestinationSelector.Legacy, DestinationSelector.Choose(Route(MigrationMode.Canary, 30), null, () => 30));
    }

    [Fact]
    public void A_canary_key_always_lands_in_the_same_bucket() =>
        Assert.All(Enumerable.Range(0, 50).Select(i => $"customer-{i}"), key =>
            Assert.Equal(DestinationSelector.Bucket(key, () => 0), DestinationSelector.Bucket(key, () => 99)));
}
