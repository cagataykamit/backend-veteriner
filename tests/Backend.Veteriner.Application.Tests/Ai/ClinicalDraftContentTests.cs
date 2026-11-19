using System.Text.Json.Nodes;
using AwesomeAssertions;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Ai.ClinicalDrafts;

namespace Backend.Veteriner.Application.Tests.Ai;

/// <summary>AI-001/003/004 taslak içeriği: model yanıtının doğrulanması, kabul edilen içeriğin okunması, istemler.</summary>
public sealed class ClinicalDraftContentTests
{
    // ----- AI-001 muayene notu -----

    [Fact]
    public void ExamNote_Parse_Keeps_Only_Known_Typed_Fields_And_Restores_Patient_Name()
    {
        var content = ExamNoteDraftContent.Parse(
            """{"visitReason":" [hasta] kusma ","findings":"","plan":null,"weightKg":4.25,"heartRateBpm":"yüz","respiratoryRatePerMin":24.5,"extra":"x"}""",
            "Karamel");

        content.Should().NotBeNull();
        var fields = content![ExamNoteDraftContent.FieldsKey]!.AsObject();
        fields.Select(p => p.Key).Should().BeEquivalentTo([ExamNoteDraftContent.VisitReason, ExamNoteDraftContent.WeightKg]);
        fields[ExamNoteDraftContent.VisitReason]!.GetValue<string>().Should().Be("Karamel kusma");
        fields[ExamNoteDraftContent.WeightKg]!.GetValue<decimal>().Should().Be(4.25m);
        content[ExamNoteDraftContent.ReplaceFieldsKey]!.AsArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("değil json")]
    [InlineData("[]")]
    [InlineData("""{"extra":"x","findings":"  "}""")]
    public void ExamNote_Parse_Returns_Null_When_Nothing_Usable(string? json)
        => ExamNoteDraftContent.Parse(json, "Karamel").Should().BeNull();

    [Fact]
    public void ExamNote_Read_Returns_Texts_Vitals_And_Replace_Selection()
    {
        var read = ExamNoteDraftContent.Read(JsonNode.Parse(
            """{"fields":{"findings":" Kızarıklık ","notes":"","temperatureC":38.5,"heartRateBpm":null},"replaceFields":["findings"]}""")!.AsObject());

        read.IsSuccess.Should().BeTrue();
        read.Value!.Texts.Should().BeEquivalentTo(new Dictionary<string, string> { [ExamNoteDraftContent.Findings] = "Kızarıklık" });
        read.Value.TemperatureC.Should().Be(38.5m);
        read.Value.HeartRateBpm.Should().BeNull();
        read.Value.Replace.Should().BeEquivalentTo([ExamNoteDraftContent.Findings]);
    }

    [Theory]
    [InlineData("""{"fields":{"kilo":3}}""")]
    [InlineData("""{"fields":{"findings":5}}""")]
    [InlineData("""{"fields":{"heartRateBpm":80.5}}""")]
    [InlineData("""{"fields":{"findings":"x"},"replaceFields":["weightKg"]}""")]
    [InlineData("""{"fields":{"findings":"x"},"extra":1}""")]
    [InlineData("""{"fields":{"findings":"  "}}""")]
    [InlineData("""{"findings":"x"}""")]
    public void ExamNote_Read_Rejects_Content_Outside_Schema(string json)
    {
        var read = ExamNoteDraftContent.Read(JsonNode.Parse(json)!.AsObject());

        read.IsSuccess.Should().BeFalse();
        read.Error.Code.Should().Be("AiDrafts.Validation");
    }

    [Fact]
    public void ExamNote_Replace_Selection_Alone_Is_Not_An_Edit()
    {
        var generated = JsonNode.Parse("""{"fields":{"findings":"x"},"replaceFields":[]}""")!.AsObject();

        ExamNoteDraftContent.IsEdited(generated, JsonNode.Parse("""{"fields":{"findings":"x"},"replaceFields":["findings"]}""")!.AsObject())
            .Should().BeFalse();
        ExamNoteDraftContent.IsEdited(generated, JsonNode.Parse("""{"fields":{"findings":"y"},"replaceFields":[]}""")!.AsObject())
            .Should().BeTrue();
    }

    // ----- AI-003 / AI-004 tek metin -----

    [Fact]
    public void Text_Parse_Restores_Patient_Name_And_Enforces_Length()
    {
        AiDraftTextContent.Parse("""{"text":" [hasta] için kontrol 3 gün sonra. "}""", "Pamuk", 100)!["text"]!.GetValue<string>()
            .Should().Be("Pamuk için kontrol 3 gün sonra.");
        AiDraftTextContent.Parse("""{"text":"[hasta] için"}""", null, 100)!["text"]!.GetValue<string>()
            .Should().Be("hastanız için");
        AiDraftTextContent.Parse("""{"text":"uzun metin"}""", "Pamuk", 5).Should().BeNull();
        AiDraftTextContent.Parse("""{"text":"  "}""", "Pamuk", 100).Should().BeNull();
        AiDraftTextContent.Parse("""{"metin":"x"}""", "Pamuk", 100).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"text":"   "}""")]
    [InlineData("""{"text":3}""")]
    [InlineData("""{"text":"x","extra":1}""")]
    [InlineData("""{"text":"çok uzun metin"}""")]
    public void Text_Read_Rejects_Invalid_Content(string json)
        => AiDraftTextContent.Read(JsonNode.Parse(json)!.AsObject(), 10).Error.Code.Should().Be("AiDrafts.Validation");

    // ----- istemler -----

    [Fact]
    public void Prompts_Have_Distinct_Versions_And_Closed_Schemas()
    {
        var prompts = new[] { ClinicalAiPrompts.ExamNote, ClinicalAiPrompts.OwnerExplanation, ClinicalAiPrompts.DischargeInstructions };

        prompts.Select(p => p.Version).Should().OnlyHaveUniqueItems().And.OnlyContain(v => System.Text.RegularExpressions.Regex.IsMatch(v, "^[a-z-]+/[0-9]+$"));
        ClinicalAiPrompts.ExamNote.Version.Should().Be("exam-note-structure/2", "dikte düzeltme kuralı eklendi (2026-11-19)");
        prompts.Should().OnlyContain(p => p.Schema()["additionalProperties"]!.GetValue<bool>() == false);
        prompts.Should().OnlyContain(p => p.System.Contains(AiTextRedactor.PatientMask) && !p.System.Contains('{'));
        ExamNoteDraftContent.Schema()["properties"]!.AsObject().Select(p => p.Key)
            .Should().Contain(ExamNoteDraftContent.TextFields);
    }

    // ----- hayvan adı yer tutucusu -----

    [Fact]
    public void Redactor_Masks_Patient_Before_Person_Names()
    {
        var patient = AiTextRedactor.PatientNameTerms("Pia");
        AiTextRedactor.Redact("Pia'nın sahibi Ayşe Pia hakkında aradı.", ["Ayşe"], patient)
            .Should().Be("[hasta]'nın sahibi [kişi] [hasta] hakkında aradı.");
        AiTextRedactor.PatientNameTerms(" ").Should().BeEmpty();
        AiTextRedactor.PatientNameTerms("Kara Boncuk").Should().Equal("Kara Boncuk", "Kara", "Boncuk");
    }
}
