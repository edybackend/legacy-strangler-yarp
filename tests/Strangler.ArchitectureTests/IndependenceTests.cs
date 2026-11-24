using System.Reflection;
using NetArchTest.Rules;

namespace Strangler.ArchitectureTests;

/// <summary>The services share a contract over HTTP, never code; the gateway routes, it holds no business rules.</summary>
public class IndependenceTests
{
    private static readonly Assembly LegacyAssembly = typeof(Legacy.Api.LegacyApiMarker).Assembly;
    private static readonly Assembly ModernAssembly = typeof(Modern.Api.ModernApiMarker).Assembly;
    private static readonly Assembly GatewayAssembly = typeof(Gateway.GatewayMarker).Assembly;

    [Fact]
    public void The_modern_service_does_not_reuse_legacy_code() => AssertNoDependency(ModernAssembly, "Legacy.Api");

    [Fact]
    public void The_legacy_service_knows_nothing_about_the_migration() => AssertNoDependency(LegacyAssembly, "Modern.Api", "Gateway");

    [Fact]
    public void The_gateway_has_no_data_access_or_business_code() =>
        AssertNoDependency(GatewayAssembly, "Legacy.Api", "Modern.Api", "Dapper", "Microsoft.Data.Sqlite");

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful, "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
