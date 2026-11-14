using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Vaccinations;

/// <summary>ADR-043 AK-3: iptal ayrı ve gerekçeli eylemdir, son durumdur; güncellemede izinli tek geçiş Planlı → Uygulandı.</summary>
public sealed class VaccinationLifecycleTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private static Vaccination New(VaccinationStatus status)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Kuduz", status,
            status == VaccinationStatus.Applied ? Now.AddHours(-1) : null,
            status == VaccinationStatus.Scheduled ? Now.AddDays(10) : null,
            null, Now);

    private static Domain.Shared.Result Update(Vaccination v, VaccinationStatus status)
        => v.UpdateDetails(v.PetId, v.ClinicId, null, v.VaccineDefinitionId!.Value, v.VaccineName, status,
            status == VaccinationStatus.Applied ? Now.AddHours(-1) : null,
            status == VaccinationStatus.Scheduled ? Now.AddDays(10) : null,
            null, Now);

    [Theory]
    [InlineData(VaccinationStatus.Scheduled, VaccinationStatus.Scheduled, true)]
    [InlineData(VaccinationStatus.Scheduled, VaccinationStatus.Applied, true)]
    [InlineData(VaccinationStatus.Applied, VaccinationStatus.Applied, true)]
    [InlineData(VaccinationStatus.Applied, VaccinationStatus.Scheduled, false)]
    [InlineData(VaccinationStatus.Scheduled, VaccinationStatus.Cancelled, false)]
    [InlineData(VaccinationStatus.Applied, VaccinationStatus.Cancelled, false)]
    public void Update_allows_only_scheduled_to_applied(VaccinationStatus from, VaccinationStatus to, bool allowed)
    {
        var v = New(from);

        var result = Update(v, to);

        result.IsSuccess.Should().Be(allowed);
        if (!allowed)
        {
            result.Error.Code.Should().Be(Vaccination.InvalidStatusTransitionCode);
            v.Status.Should().Be(from);
        }
    }

    [Fact]
    public void Cancel_requires_reason_keeps_who_when_why_and_clears_applied_time()
    {
        var v = New(VaccinationStatus.Applied);
        var user = Guid.NewGuid();

        v.Cancel(user, "kısa", Now).Error.Code.Should().Be("Vaccinations.Validation");
        v.IsCancelled.Should().BeFalse();

        v.Cancel(user, " Yanlış hastaya girildi ", Now).IsSuccess.Should().BeTrue();
        v.Status.Should().Be(VaccinationStatus.Cancelled);
        v.AppliedAtUtc.Should().BeNull();
        v.CancelledByUserId.Should().Be(user);
        v.CancelReason.Should().Be("Yanlış hastaya girildi");
        v.CancelledAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Cancelled_vaccination_is_final()
    {
        var v = New(VaccinationStatus.Scheduled);
        v.Cancel(Guid.NewGuid(), "Sahip vazgeçti", Now).IsSuccess.Should().BeTrue();

        v.Cancel(Guid.NewGuid(), "Tekrar iptal ediliyor", Now).Error.Code.Should().Be("Vaccinations.AlreadyCancelled");
        Update(v, VaccinationStatus.Scheduled).Error.Code.Should().Be("Vaccinations.AlreadyCancelled");
        Update(v, VaccinationStatus.Applied).Error.Code.Should().Be("Vaccinations.AlreadyCancelled");
    }
}
