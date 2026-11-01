using Backend.Veteriner.Domain.Examinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Examinations;

public sealed class ExaminationTemplatePrefillTests
{
    private static readonly DateTime ExaminedAt = new(2026, 10, 8, 10, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    private static Examination Draft(string visitReason = "Kontrol", string? findings = "", string? plan = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), appointmentId: null, ExaminedAt, visitReason, findings, "Değerlendirme", null, plan: plan, utcNow: Now);

    [Theory]
    [InlineData(null, "Aşı", "Aşı", true)]
    [InlineData("  ", " Aşı ", "Aşı", true)]
    [InlineData("İki gündür iştahsızlık", "Kontrol", "İki gündür iştahsızlık", false)]
    [InlineData("İki gündür iştahsızlık", null, "İki gündür iştahsızlık", false)]
    [InlineData(null, "  ", null, false)]
    public void FillIfEmpty_Should_Fill_Only_Empty_Field(string? current, string? addition, string? expected, bool filled)
        => ExaminationTextMerge.FillIfEmpty(current, addition).Should().Be((expected, filled));

    [Theory]
    [InlineData(null, "Genel durum:", "Genel durum:", 1)]
    [InlineData("", "Genel durum:", "Genel durum:", 1)]
    [InlineData("Ateş var", null, "Ateş var", 0)]
    [InlineData("Ateş var", "  ", "Ateş var", 0)]
    [InlineData("Ateş var", " Genel durum: ", "Ateş var\nGenel durum:", 1)]
    [InlineData("Not\nGENEL   DURUM:", "Genel durum:", "Not\nGENEL   DURUM:", 0)]
    [InlineData("İshal, kusma", "ISHAL, KUSMA", "İshal, kusma", 0)]
    // Satır bazında: sondaki boşluk farkı ("Genel durum: " / "Genel durum:") aynı satırdır; yeni satırlar eklenir.
    [InlineData("Önceki şikâyetin seyri: \nGenel durum: ", "Genel durum: \nVücut ısısı: \nAşıya engel bulgu: ", "Önceki şikâyetin seyri: \nGenel durum:\nVücut ısısı:\nAşıya engel bulgu:", 2)]
    // Hekim başlığı doldurmuşsa başlık tekrar eklenmez; şablon içinde aynı satır bir kez eklenir.
    [InlineData("Genel durum: iyi", "Genel durum:\nGenel durum:\nİştah:", "Genel durum: iyi\nİştah:", 1)]
    [InlineData("Genel durum", "Genel durum:", "Genel durum\nGenel durum:", 1)]
    public void MergeLines_Should_Append_Only_Missing_Lines(string? current, string? addition, string? expected, int added)
        => ExaminationTextMerge.MergeLines(current, addition).Should().Be((expected, added));

    [Fact]
    public void Prefill_Should_Append_Lines_And_Keep_Existing_Visit_Reason()
    {
        var e = Draft(findings: "Kulak kırmızı");

        var result = e.PrefillFromTemplate("Kulak kaşıma", "Kulak kanalı:\nTimpan:", "Damla 7 gün", Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ExaminationTemplatePrefill(TemplateVisitReasonOutcome.SkippedExisting, 2, 1));
        result.Value!.UpdatedFields.Should().Equal("findings", "plan");
        e.VisitReason.Should().Be("Kontrol");
        e.Findings.Should().Be("Kulak kırmızı\nKulak kanalı:\nTimpan:");
        e.Plan.Should().Be("Damla 7 gün");
        e.Assessment.Should().Be("Değerlendirme");
        e.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Prefill_Without_Template_Visit_Reason_Should_Report_NotInTemplate()
    {
        var e = Draft();

        var result = e.PrefillFromTemplate(null, "Genel durum:", null, Now);

        result.Value.Should().Be(new ExaminationTemplatePrefill(TemplateVisitReasonOutcome.NotInTemplate, 1, 0));
        result.Value!.UpdatedFields.Should().Equal("findings");
    }

    [Fact]
    public void Prefill_Twice_Should_Not_Duplicate_But_Still_Touch_UpdatedAt()
    {
        var e = Draft();
        e.PrefillFromTemplate("Aşı", "Genel durum: ", null, Now);

        var later = Now.AddMinutes(5);
        var second = e.PrefillFromTemplate("aşı", "Genel durum:", null, later);

        second.Value.Should().Be(new ExaminationTemplatePrefill(TemplateVisitReasonOutcome.SkippedExisting, 0, 0));
        second.Value!.UpdatedFields.Should().BeEmpty();
        e.VisitReason.Should().Be("Kontrol");
        e.Findings.Should().Be("Genel durum:");
        e.UpdatedAtUtc.Should().Be(later);
    }

    [Fact]
    public void Prefill_Should_Fail_On_Finalized_Examination_Without_Change()
    {
        var e = Draft();
        e.Finalize(Guid.NewGuid(), Now);

        var result = e.PrefillFromTemplate("Aşı", "Bulgu", null, Now);

        result.Error.Code.Should().Be("Examinations.Finalized");
        e.VisitReason.Should().Be("Kontrol");
        e.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Prefill_Should_Fail_When_Field_Would_Exceed_Limit()
    {
        var e = Draft(plan: new string('p', Examination.MaxPlanLength - 2));

        var result = e.PrefillFromTemplate(null, null, "Ek plan", Now);

        result.Error.Code.Should().Be("Examinations.TemplateTextTooLong");
        e.Plan.Should().HaveLength(Examination.MaxPlanLength - 2);
    }
}
