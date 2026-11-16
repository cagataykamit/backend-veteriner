using Backend.Veteriner.Application.Imports;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Imports.Reading.EVet;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>Dosya okuma notlarının partiye yazılan biçimi (IMPORT_API_CONTRACT.md §7.2.1).</summary>
public sealed class ImportWarningsDocumentTests
{
    [Fact]
    public void Notes_Should_Round_Trip_With_Table_Keys_And_Structure()
    {
        var notes = ImportWarningsDocument.From([EVetNotes.SameOwnerSameName(4, 1), ImportReadNotes.TableEmpty(ImportTable.Products, sheet: false)]);

        var parsed = ImportWarningsDocument.Parse(ImportWarningsDocument.Serialize(notes))!;

        parsed.Should().HaveCount(2);
        var decision = parsed[0];
        (decision.Code, decision.Table, decision.Severity, decision.Count, decision.Unit).Should()
            .Be((EVetNotes.SameOwnerSameNameCode, "pets", "decision", 1, "satır"));
        decision.Parts!.Select(p => (p.Label, p.Count, p.Severity)).Should().Equal(
            ("Hastası size sorulacak", 1, "decision"), ("Doğru hastaya kendiliğinden bağlandı", 4, null));
        decision.Target!.Anchor.Should().Be("required");
        decision.Message.Should().Be(notes[0].Message);

        (parsed[1].Code, parsed[1].Table, parsed[1].Severity, parsed[1].Count).Should().Be(("Imports.TableEmpty", "products", "skipped", null));
    }

    [Fact]
    public void Unstructured_Note_Should_Be_Written_Without_Empty_Fields()
    {
        var json = ImportWarningsDocument.Serialize(ImportWarningsDocument.From([new ImportReadWarning("Imports.Other", null, "Metin")]));

        json.Should().Be("[{\"code\":\"Imports.Other\",\"table\":null,\"message\":\"Metin\"}]");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Batch_Without_Stored_Notes_Should_Have_No_Notes(string? json)
        => ImportWarningsDocument.Parse(json).Should().BeNull();
}
