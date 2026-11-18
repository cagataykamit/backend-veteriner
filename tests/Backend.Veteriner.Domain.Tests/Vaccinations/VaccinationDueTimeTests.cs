using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Vaccinations;

/// <summary>
/// Vade saatinin gerçekliği (APPOINTMENT_CALENDAR_API_CONTRACT.md §8.1): içe aktarılan saatli vade işaretlenir; panelden girilen vade
/// saatsizdir; düzenlemede vade değişmezse bayrak korunur, değişirse düşer.
/// </summary>
public sealed class VaccinationDueTimeTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Due = new(2026, 12, 1, 7, 14, 0, DateTimeKind.Utc);

    private static Vaccination Imported(DateTime? due, bool timeSpecified)
        => Vaccination.CreateImported(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Karma", Now.AddDays(-30), due, null,
            Guid.NewGuid(), Now, timeSpecified);

    private static void UpdateDue(Vaccination v, DateTime? due)
        => v.UpdateDetails(v.PetId, v.ClinicId, null, v.VaccineDefinitionId!.Value, v.VaccineName, v.Status, v.AppliedAtUtc, due, "not",
            Now).IsSuccess.Should().BeTrue();

    [Fact]
    public void Panel_created_vaccination_has_no_specified_time()
    {
        var v = new Vaccination(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Kuduz", VaccinationStatus.Scheduled,
            null, Due, null, Now);

        v.DueTimeSpecified.Should().BeFalse();
    }

    [Fact]
    public void Imported_due_keeps_time_flag_only_when_due_exists()
    {
        Imported(Due, timeSpecified: true).DueTimeSpecified.Should().BeTrue();
        Imported(null, timeSpecified: true).DueTimeSpecified.Should().BeFalse("vadesiz kayıtta saat yoktur");
    }

    [Fact]
    public void Update_keeps_flag_when_due_unchanged_and_drops_it_when_due_changes()
    {
        var v = Imported(Due, timeSpecified: true);

        UpdateDue(v, Due);
        v.DueTimeSpecified.Should().BeTrue("yalnız not değişti");

        UpdateDue(v, Due.AddDays(1));
        v.DueTimeSpecified.Should().BeFalse("panelden girilen vade saatsiz sayılır");

        UpdateDue(v, Due);
        v.DueTimeSpecified.Should().BeFalse("bayrak geri gelmez");
    }
}
