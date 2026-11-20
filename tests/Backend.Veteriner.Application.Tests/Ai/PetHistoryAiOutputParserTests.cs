using System.Text.Json.Nodes;
using AwesomeAssertions;
using Backend.Veteriner.Application.Ai.PetHistorySummary;

namespace Backend.Veteriner.Application.Tests.Ai;

public sealed class PetHistoryAiOutputParserTests
{
    private static readonly Guid PetId = Guid.NewGuid();
    private static readonly Guid ExamId = Guid.NewGuid();
    private static readonly Guid LabId = Guid.NewGuid();
    private static readonly DateTime ExamAt = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, PetHistoryAiSourceDto> Sources =
        new Dictionary<string, PetHistoryAiSourceDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["P1"] = new("Pet", PetId, null),
            ["E1"] = new("Examination", ExamId, ExamAt),
            ["L1"] = new("LabResult", LabId, ExamAt),
        };

    [Fact]
    public void Maps_Refs_To_Sources_And_Orders_Sections()
    {
        var json = """
            {"sections":[
              {"kind":"LabResults","items":[{"text":"Biyokimya normal.","refs":["L1"]}]},
              {"kind":"Alerts","items":[{"text":"Penisilin alerjisi.","refs":["P1","E1"]}]}
            ]}
            """;

        var result = PetHistoryAiOutputParser.Parse(json, Sources)!;

        result.DroppedItemCount.Should().Be(0);
        result.Sections.Select(s => s.Kind).Should().Equal(PetHistoryAiSectionKind.Alerts, PetHistoryAiSectionKind.LabResults);
        result.Sections[0].Title.Should().Be("Uyarılar");
        result.Sections[0].Items.Single().Sources.Should().Equal(Sources["P1"], Sources["E1"]);
    }

    [Fact]
    public void Drops_Fabricated_Refs_And_Items_Without_Valid_Source()
    {
        var json = """
            {"sections":[{"kind":"RecentVisits","items":[
              {"text":"Kusma ile geldi.","refs":["E1","E9"]},
              {"text":"Uydurma bilgi.","refs":["X1"]},
              {"text":"Kaynaksız.","refs":[]}
            ]}]}
            """;

        var result = PetHistoryAiOutputParser.Parse(json, Sources)!;

        result.DroppedItemCount.Should().Be(2);
        var item = result.Sections.Single().Items.Single();
        item.Text.Should().Be("Kusma ile geldi.");
        item.Sources.Should().ContainSingle().Which.Id.Should().Be(ExamId);
    }

    [Fact]
    public void Merges_Repeated_Section_Skips_Unknown_Kind_And_Caps_Items_And_Text()
    {
        var longText = new string('a', PetHistoryAiPrompt.MaxItemTextLength + 50);
        var items = new JsonArray(Enumerable.Range(0, PetHistoryAiPrompt.MaxItemsPerSection + 2)
            .Select(i => (JsonNode)new JsonObject { ["text"] = i == 0 ? longText : $"madde {i}", ["refs"] = new JsonArray("E1") })
            .ToArray());
        var json = new JsonObject
        {
            ["sections"] = new JsonArray(
                new JsonObject { ["kind"] = "Medications", ["items"] = items },
                new JsonObject { ["kind"] = "Diagnosis", ["items"] = new JsonArray(new JsonObject { ["text"] = "x", ["refs"] = new JsonArray("E1") }) },
                new JsonObject { ["kind"] = "Medications", ["items"] = new JsonArray(new JsonObject { ["text"] = "fazla", ["refs"] = new JsonArray("E1") }) }),
        }.ToJsonString();

        var result = PetHistoryAiOutputParser.Parse(json, Sources)!;

        var section = result.Sections.Should().ContainSingle().Which;
        section.Kind.Should().Be(PetHistoryAiSectionKind.Medications);
        section.Items.Should().HaveCount(PetHistoryAiPrompt.MaxItemsPerSection);
        section.Items[0].Text.Should().HaveLength(PetHistoryAiPrompt.MaxItemTextLength + 1).And.EndWith("…");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("özet değil")]
    [InlineData("{\"summary\":\"x\"}")]
    [InlineData("[]")]
    public void Returns_Null_For_Invalid_Output(string? json)
        => PetHistoryAiOutputParser.Parse(json, Sources).Should().BeNull();

    [Fact]
    public void Empty_Sections_Is_Valid_And_Empty()
    {
        var result = PetHistoryAiOutputParser.Parse("""{"sections":[{"kind":"Alerts","items":[]}]}""", Sources)!;

        result.Sections.Should().BeEmpty();
        result.DroppedItemCount.Should().Be(0);
    }
}
