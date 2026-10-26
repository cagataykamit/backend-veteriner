using Backend.Veteriner.Domain.Examinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Examinations;

public sealed class ExaminationTemplatePrefillTests
{
    private static readonly DateTime ExaminedAt = new(2026, 10, 8, 10, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    private static Examination Draft(string visitReason = "Kontrol", string? findings = "", string? plan = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), appointmentId: null, ExaminedAt, visitReason, findings, "Değerlendirme", null, plan: plan);

    [Theory]
    [InlineData(null, "Genel durum:", "Genel durum:")]
    [InlineData("", "Genel durum:", "Genel durum:")]
    [InlineData("Ateş var", null, "Ateş var")]
    [InlineData("Ateş var", "  ", "Ateş var")]
    [InlineData("Ateş var", " Genel durum: ", "Ateş var\nGenel durum:")]
    [InlineData("Not\nGENEL   DURUM:", "Genel durum:", "Not\nGENEL   DURUM:")]
    [InlineData("İshal, kusma", "ISHAL", "İshal, kusma")]
    public void Merge_Should_Keep_Existing_Text_And_Not_Duplicate(string? current, string? addition, string? expected)
        => ExaminationTextMerge.Merge(current, addition, ExaminationTextMerge.BlockSeparator).Should().Be(expected);

    [Fact]
    public void Prefill_Should_Append_And_Report_Changed_Fields()
    {
        var e = Draft(findings: "Kulak kırmızı");

        var result = e.PrefillFromTemplate("Kulak kaşıma", "Kulak kanalı:", "Damla 7 gün", Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal("visitReason", "findings", "plan");
        e.VisitReason.Should().Be("Kontrol, Kulak kaşıma");
        e.Findings.Should().Be("Kulak kırmızı\nKulak kanalı:");
        e.Plan.Should().Be("Damla 7 gün");
        e.Assessment.Should().Be("Değerlendirme");
        e.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Prefill_Twice_Should_Not_Duplicate_But_Still_Touch_UpdatedAt()
    {
        var e = Draft();
        e.PrefillFromTemplate("Aşı", "Genel durum:", null, Now);

        var later = Now.AddMinutes(5);
        var second = e.PrefillFromTemplate("aşı", "Genel durum:", null, later);

        second.Value.Should().BeEmpty();
        e.VisitReason.Should().Be("Kontrol, Aşı");
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
