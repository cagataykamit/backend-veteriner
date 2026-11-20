using System.Text.Json.Nodes;
using AwesomeAssertions;
using Backend.Veteriner.Application.Ai.PetHistorySummary;

namespace Backend.Veteriner.Application.Tests.Ai;

public sealed class PetHistoryAiPromptTests
{
    [Fact]
    public void Schema_Closes_Every_Object_And_Lists_Every_Section_Kind()
    {
        var schema = PetHistoryAiPrompt.Schema();

        Objects(schema).Should().NotBeEmpty().And.OnlyContain(o => o["additionalProperties"]!.GetValue<bool>() == false);
        var kinds = schema["properties"]!["sections"]!["items"]!["properties"]!["kind"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>());
        kinds.Should().Equal(Enum.GetNames<PetHistoryAiSectionKind>());
    }

    [Fact]
    public void Every_Section_Kind_Has_A_Title()
        => PetHistoryAiPrompt.Titles.Keys.Should().BeEquivalentTo(Enum.GetValues<PetHistoryAiSectionKind>());

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["type"]?.GetValue<string>() == "object")
                yield return obj;
            foreach (var (_, child) in obj)
                foreach (var nested in Objects(child))
                    yield return nested;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                foreach (var nested in Objects(child))
                    yield return nested;
        }
    }
}
