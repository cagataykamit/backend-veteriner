using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.VitalMeasurements;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.VitalMeasurements;

/// <summary>HOSP-004: vital değer aralıkları tek yerde (<see cref="VitalSigns"/>), gerekçeli iptal ve geç kayıt (ADR-043).</summary>
public sealed class VitalMeasurementTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

    private static VitalSigns Values(decimal? t = 38.6m, int? hr = 120, int? rr = 24, decimal? w = 12.45m)
        => VitalSigns.Create(t, hr, rr, w).Value!;

    private static VitalMeasurement New(Guid? hospitalizationId = null, Guid? examinationId = null)
        => VitalMeasurement.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), hospitalizationId, examinationId, Now.AddHours(-1), Values(), " Sakin ", Guid.NewGuid(), Now).Value!;

    [Fact]
    public void Values_require_at_least_one_and_respect_ranges()
    {
        VitalSigns.Create(null, null, null, null).Error.Code.Should().Be(VitalSigns.ValueRequiredCode);

        VitalSigns.Create(24.9m, null, null, null).Error.Code.Should().Be(VitalSigns.ValidationCode);
        VitalSigns.Create(45.1m, null, null, null).Error.Code.Should().Be(VitalSigns.ValidationCode);
        VitalSigns.Create(null, 0, null, null).Error.Code.Should().Be(VitalSigns.ValidationCode);
        VitalSigns.Create(null, 401, null, null).Error.Code.Should().Be(VitalSigns.ValidationCode);
        VitalSigns.Create(null, null, 0, null).Error.Code.Should().Be(VitalSigns.ValidationCode);
        VitalSigns.Create(null, null, 201, null).Error.Code.Should().Be(VitalSigns.ValidationCode);
        VitalSigns.Create(null, null, null, 0.004m).Error.Code.Should().Be(VitalSigns.ValidationCode);
        VitalSigns.Create(null, null, null, 10000m).Error.Code.Should().Be(VitalSigns.ValidationCode);

        VitalSigns.Create(25m, 1, 1, 0.01m).IsSuccess.Should().BeTrue();
        VitalSigns.Create(45m, 400, 200, 9999.99m).IsSuccess.Should().BeTrue();
        VitalSigns.Create(null, null, null, 450m).IsSuccess.Should().BeTrue("at ve sığır kilosu da yazılabilir");
    }

    [Fact]
    public void Values_are_rounded_before_range_check()
    {
        var v = VitalSigns.Create(38.25m, null, null, 12.345m).Value!;
        v.TemperatureC.Should().Be(38.3m);
        v.WeightKg.Should().Be(12.35m);

        VitalSigns.Create(45.04m, null, null, null).Value!.TemperatureC.Should().Be(45.0m);
        VitalSigns.Create(45.05m, null, null, null).Error.Code.Should().Be(VitalSigns.ValidationCode);
    }

    [Fact]
    public void Create_rejects_both_links_and_long_note_and_trims_note()
    {
        VitalMeasurement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, Values(), null, Guid.NewGuid(), Now)
            .Error.Code.Should().Be(VitalMeasurement.ValidationCode);
        VitalMeasurement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Now, Values(), new string('x', 501), Guid.NewGuid(), Now)
            .Error.Code.Should().Be(VitalMeasurement.ValidationCode);

        var m = New();
        m.Note.Should().Be("Sakin");
        m.MeasuredAtUtc.Should().Be(Now.AddHours(-1));
        m.TemperatureC.Should().Be(38.6m);
        m.IsCancelled.Should().BeFalse();
        m.AddedAfterFinalization.Should().BeFalse();
    }

    [Fact]
    public void Update_replaces_values_and_note()
    {
        var m = New();

        m.Update(Now, VitalSigns.Create(null, 90, null, null).Value!, "  ", Now).IsSuccess.Should().BeTrue();

        m.TemperatureC.Should().BeNull();
        m.HeartRateBpm.Should().Be(90);
        m.RespiratoryRatePerMin.Should().BeNull();
        m.WeightKg.Should().BeNull();
        m.Note.Should().BeNull();
        m.MeasuredAtUtc.Should().Be(Now);
        m.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Cancel_requires_reason_and_is_final()
    {
        var m = New();
        var user = Guid.NewGuid();

        m.Cancel(user, "kısa", Now).Error.Code.Should().Be(VitalMeasurement.ValidationCode);
        m.IsCancelled.Should().BeFalse();

        m.Cancel(user, " Yanlış hastaya girildi ", Now).IsSuccess.Should().BeTrue();
        m.CancelledByUserId.Should().Be(user);
        m.CancelReason.Should().Be("Yanlış hastaya girildi");

        m.Cancel(user, "Tekrar iptal ediliyor", Now).Error.Code.Should().Be(VitalMeasurement.AlreadyCancelledCode);
        m.Update(Now, Values(), null, Now).Error.Code.Should().Be(VitalMeasurement.AlreadyCancelledCode);
    }

    [Fact]
    public void Late_entry_requires_reason()
    {
        var m = New(examinationId: Guid.NewGuid());

        m.MarkAddedAfterFinalization(null).Error.Code.Should().Be(VitalMeasurement.LateEntryReasonRequiredCode);
        m.AddedAfterFinalization.Should().BeFalse();

        m.MarkAddedAfterFinalization(" Kesinleştirmeden sonra ölçüldü ").IsSuccess.Should().BeTrue();
        m.AddedAfterFinalization.Should().BeTrue();
        m.LateEntryReason.Should().Be("Kesinleştirmeden sonra ölçüldü");
    }

    [Fact]
    public void Hospitalization_covers_stay_inclusive_and_open_until_discharge()
    {
        var admitted = Now.AddDays(-2);
        var h = new Hospitalization(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, admitted, null, "Gözlem", null, Now);

        h.CoversInstant(admitted.AddTicks(-1)).Should().BeFalse();
        h.CoversInstant(admitted).Should().BeTrue();
        h.CoversInstant(Now.AddDays(10)).Should().BeTrue("taburcu yoksa üst sınır yok");

        h.Discharge(Now.AddDays(-1), null, Now).IsSuccess.Should().BeTrue();
        h.CoversInstant(Now.AddDays(-1)).Should().BeTrue();
        h.CoversInstant(Now.AddDays(-1).AddTicks(1)).Should().BeFalse();
    }
}
