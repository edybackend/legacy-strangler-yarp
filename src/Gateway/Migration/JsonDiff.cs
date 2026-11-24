using System.Text.Json.Nodes;

namespace Gateway.Migration;

public sealed record Difference(string Path, string? Legacy, string? Modern);

/// <summary>
/// Compares two JSON documents by meaning: property order is ignored and numbers are compared by value
/// (<c>1169.1</c> equals <c>1169.10</c>), so only real differences are reported.
/// </summary>
public static class JsonDiff
{
    public static IReadOnlyList<Difference> Compare(JsonNode? legacy, JsonNode? modern)
    {
        var differences = new List<Difference>();
        Walk("$", legacy, modern, differences);
        return differences;
    }

    private static void Walk(string path, JsonNode? a, JsonNode? b, List<Difference> differences)
    {
        switch (a, b)
        {
            case (null, null):
                return;
            case (JsonObject oa, JsonObject ob):
                foreach (var name in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)).Order(StringComparer.Ordinal))
                {
                    Walk($"{path}.{name}", oa[name], ob[name], differences);
                }

                return;
            case (JsonArray aa, JsonArray ab):
                for (var i = 0; i < Math.Max(aa.Count, ab.Count); i++)
                {
                    Walk($"{path}[{i}]", i < aa.Count ? aa[i] : null, i < ab.Count ? ab[i] : null, differences);
                }

                return;
            case (JsonValue va, JsonValue vb) when SameValue(va, vb):
                return;
            default:
                differences.Add(new Difference(path, a?.ToJsonString(), b?.ToJsonString()));
                return;
        }
    }

    private static bool SameValue(JsonValue a, JsonValue b) =>
        a.TryGetValue<decimal>(out var da) && b.TryGetValue<decimal>(out var db)
            ? da == db
            : a.ToJsonString() == b.ToJsonString();
}
