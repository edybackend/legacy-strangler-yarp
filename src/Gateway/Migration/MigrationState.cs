using System.Collections.Concurrent;

namespace Gateway.Migration;

/// <summary>Where a route's traffic goes.</summary>
public enum MigrationMode
{
    /// <summary>Everything goes to the legacy service.</summary>
    Legacy,

    /// <summary>Clients get the legacy answer; the same request is also sent to the modern service and the answers are compared.</summary>
    Shadow,

    /// <summary>A percentage of requests goes to the modern service.</summary>
    Canary,

    /// <summary>Everything goes to the modern service.</summary>
    Modern,
}

/// <summary>A route as configured in <c>Migration:Routes</c>.</summary>
public sealed class RouteMigrationOptions
{
    public MigrationMode Mode { get; set; }

    public int CanaryPercent { get; set; }

    /// <summary>Whether the modern service implements this route yet.</summary>
    public bool ModernReady { get; set; }

    /// <summary>Only safe (GET) routes can be shadowed: shadowing a write would execute it twice.</summary>
    public bool ReadOnly { get; set; }
}

public sealed record RouteMigration(string RouteId, MigrationMode Mode, int CanaryPercent, bool ModernReady, bool ReadOnly);

public enum ChangeResult
{
    Changed,
    UnknownRoute,
    ModernNotReady,
    WritesCannotBeShadowed,
    InvalidPercent,
}

/// <summary>The migration plan, changeable at runtime without redeploying. Thread-safe; reads are lock-free.</summary>
public sealed class MigrationState
{
    private readonly ConcurrentDictionary<string, RouteMigration> _routes;

    public MigrationState(IReadOnlyDictionary<string, RouteMigrationOptions> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = new ConcurrentDictionary<string, RouteMigration>(
            routes.Select(r => KeyValuePair.Create(r.Key, new RouteMigration(r.Key, r.Value.Mode, r.Value.CanaryPercent, r.Value.ModernReady, r.Value.ReadOnly))),
            StringComparer.OrdinalIgnoreCase);
        foreach (var route in _routes.Values)
        {
            var problem = Validate(route, route.Mode, route.CanaryPercent);
            if (problem != ChangeResult.Changed)
            {
                throw new InvalidOperationException($"Migration:Routes:{route.RouteId} is invalid: {problem}.");
            }
        }
    }

    public IReadOnlyList<RouteMigration> All => [.. _routes.Values.OrderBy(r => r.RouteId, StringComparer.Ordinal)];

    public RouteMigration? Get(string routeId) => _routes.GetValueOrDefault(routeId);

    public ChangeResult Change(string routeId, MigrationMode mode, int canaryPercent)
    {
        if (!_routes.TryGetValue(routeId, out var current))
        {
            return ChangeResult.UnknownRoute;
        }

        var problem = Validate(current, mode, canaryPercent);
        if (problem == ChangeResult.Changed)
        {
            _routes[routeId] = current with { Mode = mode, CanaryPercent = mode == MigrationMode.Canary ? canaryPercent : 0 };
        }

        return problem;
    }

    private static ChangeResult Validate(RouteMigration route, MigrationMode mode, int canaryPercent) => mode switch
    {
        MigrationMode.Shadow when !route.ReadOnly => ChangeResult.WritesCannotBeShadowed,
        MigrationMode.Shadow or MigrationMode.Canary or MigrationMode.Modern when !route.ModernReady => ChangeResult.ModernNotReady,
        MigrationMode.Canary when canaryPercent is < 0 or > 100 => ChangeResult.InvalidPercent,
        _ => ChangeResult.Changed,
    };
}
