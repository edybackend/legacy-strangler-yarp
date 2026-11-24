using System.Text.Json.Nodes;
using Gateway.Migration;

namespace Strangler.UnitTests;

public class JsonDiffTests
{
    private static IReadOnlyList<Difference> Diff(string legacy, string modern) => JsonDiff.Compare(JsonNode.Parse(legacy), JsonNode.Parse(modern));

    [Fact]
    public void Property_order_does_not_matter() =>
        Assert.Empty(Diff("""{"id":1,"name":"Ana"}""", """{"name":"Ana","id":1}"""));

    [Fact]
    public void Numbers_are_compared_by_value() =>
        Assert.Empty(Diff("""{"total":1169.1}""", """{"total":1169.10}"""));

    [Fact]
    public void A_different_value_is_reported_with_its_path_and_both_values()
    {
        var difference = Assert.Single(Diff("""{"items":[{"total":1169.09}]}""", """{"items":[{"total":1169.10}]}"""));

        Assert.Equal(new Difference("$.items[0].total", "1169.09", "1169.10"), difference);
    }

    [Fact]
    public void Missing_properties_and_extra_array_items_are_differences()
    {
        var differences = Diff("""{"a":1,"list":[1]}""", """{"b":1,"list":[1,2]}""");

        Assert.Equal(["$.a", "$.b", "$.list[1]"], differences.Select(d => d.Path));
    }

    [Fact]
    public void Strings_are_compared_exactly() =>
        Assert.Single(Diff("""{"placedAt":"2026-10-02T14:10:00Z"}""", """{"placedAt":"2026-10-02T14:10:00+00:00"}"""));
}
