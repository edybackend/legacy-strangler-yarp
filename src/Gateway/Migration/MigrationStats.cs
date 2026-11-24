using System.Collections.Concurrent;

namespace Gateway.Migration;

public sealed record Mismatch(DateTimeOffset At, string RouteId, string Path, IReadOnlyList<Difference> Differences);

public sealed record RouteCounters(long ServedByLegacy, long ServedByModern, long ShadowCompared, long ShadowMismatches, long ShadowErrors);

/// <summary>Counters per route and the latest shadow mismatches (kept in memory: they are evidence for a decision, not data).</summary>
public sealed class MigrationStats
{
    private const int MaxMismatches = 100;
    private readonly ConcurrentDictionary<string, Counters> _counters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<Mismatch> _mismatches = new();

    public void Served(string routeId, string destination)
    {
        var c = _counters.GetOrAdd(routeId, _ => new Counters());
        if (destination == DestinationSelector.Modern)
        {
            Interlocked.Increment(ref c.Modern);
        }
        else
        {
            Interlocked.Increment(ref c.Legacy);
        }
    }

    public void ShadowCompared(Mismatch? mismatch, string routeId)
    {
        var c = _counters.GetOrAdd(routeId, _ => new Counters());
        Interlocked.Increment(ref c.Compared);
        if (mismatch is null)
        {
            return;
        }

        Interlocked.Increment(ref c.Mismatches);
        _mismatches.Enqueue(mismatch);
        while (_mismatches.Count > MaxMismatches && _mismatches.TryDequeue(out _))
        {
        }
    }

    public void ShadowFailed(string routeId) => Interlocked.Increment(ref _counters.GetOrAdd(routeId, _ => new Counters()).Errors);

    public RouteCounters For(string routeId) =>
        _counters.TryGetValue(routeId, out var c)
            ? new RouteCounters(Interlocked.Read(ref c.Legacy), Interlocked.Read(ref c.Modern), Interlocked.Read(ref c.Compared), Interlocked.Read(ref c.Mismatches), Interlocked.Read(ref c.Errors))
            : new RouteCounters(0, 0, 0, 0, 0);

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Mismatch> Mismatches(string? routeId) =>
        [.. _mismatches.Reverse().Where(m => routeId is null || string.Equals(m.RouteId, routeId, StringComparison.OrdinalIgnoreCase))];

    private sealed class Counters
    {
        public long Legacy;
        public long Modern;
        public long Compared;
        public long Mismatches;
        public long Errors;
    }
}
