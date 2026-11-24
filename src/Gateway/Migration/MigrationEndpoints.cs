namespace Gateway.Migration;

public sealed record RouteStatus(
    string RouteId,
    MigrationMode Mode,
    int CanaryPercent,
    bool ModernReady,
    bool ReadOnly,
    RouteCounters Counters);

public sealed record ChangeRoute(MigrationMode Mode, int CanaryPercent);

public static class MigrationEndpoints
{
    public static IEndpointRouteBuilder MapMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        var migration = app.MapGroup("/migration/api").WithTags("Migration");

        migration.MapGet("/routes", (MigrationState state, MigrationStats stats) =>
            TypedResults.Ok(state.All.Select(r => new RouteStatus(r.RouteId, r.Mode, r.CanaryPercent, r.ModernReady, r.ReadOnly, stats.For(r.RouteId)))))
            .WithSummary("Every proxied route: mode, counters and shadow results");

        migration.MapPut("/routes/{routeId}", (string routeId, ChangeRoute request, MigrationState state) =>
            state.Change(routeId, request.Mode, request.CanaryPercent) switch
            {
                ChangeResult.Changed => Results.Ok(state.Get(routeId)),
                ChangeResult.UnknownRoute => Results.Problem($"Route '{routeId}' is not proxied.", statusCode: StatusCodes.Status404NotFound),
                ChangeResult.ModernNotReady => Results.Problem($"The modern service does not implement '{routeId}' yet.", statusCode: StatusCodes.Status409Conflict),
                ChangeResult.WritesCannotBeShadowed => Results.Problem("A write route cannot be shadowed: the request would execute twice.", statusCode: StatusCodes.Status409Conflict),
                _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["canaryPercent"] = ["Must be between 0 and 100."] }),
            })
            .WithSummary("Move a route: Legacy → Shadow → Canary (n %) → Modern, or back (rollback is the same call)");

        migration.MapGet("/mismatches", (string? routeId, MigrationStats stats) => TypedResults.Ok(stats.Mismatches(routeId)))
            .WithSummary("Latest shadow mismatches, newest first, with the differing JSON paths");

        return app;
    }
}
