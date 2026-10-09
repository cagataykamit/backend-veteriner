using Backend.Veteriner.Domain.Examinations;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Examinations;

public sealed class ExaminationClinicalUpdateTests
{
    private static readonly DateTime ExaminedAt = new(2026, 10, 8, 10, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime VitalsAt = new(2026, 10, 8, 10, 45, 0, DateTimeKind.Utc);

    private static Examination CreateFilled()
        => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), appointmentId: null,
            ExaminedAt, "Iştahsızlık", "Bulgu", "Değerlendirme", "Not",
            anamnesis: "Hikaye", plan: "Plan",
            weightKg: 4.25m, temperatureC: 38.6m, heartRateBpm: 120, respiratoryRatePerMin: 30,
            vitalsMeasuredAtUtc: VitalsAt);

    [Fact]
    public void Partial_update_with_only_required_fields_keeps_all_other_fields()
    {
        var e = CreateFilled();

        var result = e.UpdateClinicalContent(new ExaminationClinicalUpdate(ExaminedAt.AddHours(1), "Yeni neden"));

        result.IsSuccess.Should().BeTrue();
        e.VisitReason.Should().Be("Yeni neden");
        e.ExaminedAtUtc.Should().Be(ExaminedAt.AddHours(1));
        e.Findings.Should().Be("Bulgu");
        e.Assessment.Should().Be("Değerlendirme");
        e.Notes.Should().Be("Not");
        e.Anamnesis.Should().Be("Hikaye");
        e.Plan.Should().Be("Plan");
        e.WeightKg.Should().Be(4.25m);
        e.TemperatureC.Should().Be(38.6m);
        e.HeartRateBpm.Should().Be(120);
        e.RespiratoryRatePerMin.Should().Be(30);
        e.VitalsMeasuredAtUtc.Should().Be(VitalsAt);
        e.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Partial_update_changes_only_supplied_text_field()
    {
        var e = CreateFilled();

        e.UpdateClinicalContent(new ExaminationClinicalUpdate(ExaminedAt, "Iştahsızlık", Plan: "  Yeni plan  "))
            .IsSuccess.Should().BeTrue();

        e.Plan.Should().Be("Yeni plan");
        e.Findings.Should().Be("Bulgu");
        e.Anamnesis.Should().Be("Hikaye");
        e.Assessment.Should().Be("Değerlendirme");
        e.Notes.Should().Be("Not");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_clears_optional_text_fields_and_findings_becomes_empty_string(string blank)
    {
        var e = CreateFilled();

        e.UpdateClinicalContent(new ExaminationClinicalUpdate(
                ExaminedAt, "Iştahsızlık",
                Findings: blank, Assessment: blank, Notes: blank, Anamnesis: blank, Plan: blank))
            .IsSuccess.Should().BeTrue();

        e.Findings.Should().BeEmpty();
        e.Assessment.Should().BeNull();
        e.Notes.Should().BeNull();
        e.Anamnesis.Should().BeNull();
        e.Plan.Should().BeNull();
    }

    [Fact]
    public void Partial_vital_update_changes_only_supplied_vital_and_keeps_measured_time()
    {
        var e = CreateFilled();

        e.UpdateClinicalContent(new ExaminationClinicalUpdate(ExaminedAt.AddHours(2), "Iştahsızlık", WeightKg: 5m))
            .IsSuccess.Should().BeTrue();

        e.WeightKg.Should().Be(5m);
        e.TemperatureC.Should().Be(38.6m);
        e.HeartRateBpm.Should().Be(120);
        e.RespiratoryRatePerMin.Should().Be(30);
        e.VitalsMeasuredAtUtc.Should().Be(VitalsAt);
    }

    [Fact]
    public void ClearVitals_clears_all_vitals_and_measured_time_but_keeps_text()
    {
        var e = CreateFilled();

        e.UpdateClinicalContent(new ExaminationClinicalUpdate(ExaminedAt, "Iştahsızlık", ClearVitals: true))
            .IsSuccess.Should().BeTrue();

        e.WeightKg.Should().BeNull();
        e.TemperatureC.Should().BeNull();
        e.HeartRateBpm.Should().BeNull();
        e.RespiratoryRatePerMin.Should().BeNull();
        e.VitalsMeasuredAtUtc.Should().BeNull();
        e.Findings.Should().Be("Bulgu");
        e.Plan.Should().Be("Plan");
    }

    [Fact]
    public void ClearVitals_with_vital_value_or_measured_time_is_rejected_without_changes()
    {
        var withValue = CreateFilled();
        var withTime = CreateFilled();

        var r1 = withValue.UpdateClinicalContent(
            new ExaminationClinicalUpdate(ExaminedAt, "Yeni", WeightKg: 5m, ClearVitals: true));
        var r2 = withTime.UpdateClinicalContent(
            new ExaminationClinicalUpdate(ExaminedAt, "Yeni", VitalsMeasuredAtUtc: VitalsAt, ClearVitals: true));

        r1.IsSuccess.Should().BeFalse();
        r1.Error.Code.Should().Be("Examinations.Validation");
        r2.IsSuccess.Should().BeFalse();
        r2.Error.Code.Should().Be("Examinations.Validation");
        withValue.VisitReason.Should().Be("Iştahsızlık");
        withValue.WeightKg.Should().Be(4.25m);
    }

    [Fact]
    public void Vitals_added_for_first_time_default_measured_time_to_examined_time()
    {
        var e = new Examination(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExaminedAt, "Kontrol", "", null, null);

        var newExaminedAt = ExaminedAt.AddHours(1);
        e.UpdateClinicalContent(new ExaminationClinicalUpdate(newExaminedAt, "Kontrol", TemperatureC: 38.5m))
            .IsSuccess.Should().BeTrue();

        e.TemperatureC.Should().Be(38.5m);
        e.VitalsMeasuredAtUtc.Should().Be(newExaminedAt);
    }

    [Fact]
    public void Measured_time_without_any_resulting_vital_is_rejected()
    {
        var e = new Examination(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExaminedAt, "Kontrol", "", null, null);

        var result = e.UpdateClinicalContent(
            new ExaminationClinicalUpdate(ExaminedAt, "Kontrol", VitalsMeasuredAtUtc: VitalsAt));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Validation");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_vital_is_rejected_and_existing_value_is_kept(int heartRate)
    {
        var e = CreateFilled();

        var result = e.UpdateClinicalContent(
            new ExaminationClinicalUpdate(ExaminedAt, "Iştahsızlık", HeartRateBpm: heartRate));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Validation");
        e.HeartRateBpm.Should().Be(120);
    }

    [Fact]
    public void Blank_visit_reason_is_rejected()
    {
        var e = CreateFilled();

        var result = e.UpdateClinicalContent(new ExaminationClinicalUpdate(ExaminedAt, "  "));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Validation");
    }
}
