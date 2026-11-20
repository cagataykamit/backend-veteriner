using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Vaccinations;

/// <summary>
/// Vade saatinin gerçekliği (APPOINTMENT_CALENDAR_API_CONTRACT.md §8.1): içe aktarılan saatli vade ve panelden tarih ve saatle girilen
/// vade işaretlenir; bildirilmeyen vade (gün bazlı plan, eski istemci) saatsizdir; düzenlemede bildirilmeyen vade değişmezse bayrak
/// korunur, değişirse düşer.
/// </summary>
public sealed class VaccinationDueTimeTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Due = new(2026, 12, 1, 7, 14, 0, DateTimeKind.Utc);

    private static Vaccination Imported(DateTime? due, bool timeSpecified)
        => Vaccination.CreateImported(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Karma", Now.AddDays(-30), due, null,
            Guid.NewGuid(), Now, timeSpecified);

    private static Vaccination Created(DateTime? due, bool timeSpecified = false)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Kuduz", VaccinationStatus.Scheduled,
            null, due, null, Now, timeSpecified);

    private static void UpdateDue(Vaccination v, DateTime? due, bool timeSpecified = false)
        => v.UpdateDetails(v.PetId, v.ClinicId, null, v.VaccineDefinitionId!.Value, v.VaccineName, v.Status, v.AppliedAtUtc, due, "not",
            Now, timeSpecified).IsSuccess.Should().BeTrue();

    [Fact]
    public void Created_vaccination_has_specified_time_only_when_caller_says_so()
    {
        Created(Due).DueTimeSpecified.Should().BeFalse("gün bazlı plan ve eski istemci bildirmez");
        Created(Due, timeSpecified: true).DueTimeSpecified.Should().BeTrue("panel formu tarih ve saat ister");
        Created(null, timeSpecified: true).DueTimeSpecified.Should().BeFalse("vadesiz kayıtta saat yoktur");
    }

    [Fact]
    public void Imported_due_keeps_time_flag_only_when_due_exists()
    {
        Imported(Due, timeSpecified: true).DueTimeSpecified.Should().BeTrue();
        Imported(null, timeSpecified: true).DueTimeSpecified.Should().BeFalse("vadesiz kayıtta saat yoktur");
    }

    [Fact]
    public void Update_without_flag_keeps_it_when_due_unchanged_and_drops_it_when_due_changes()
    {
        var v = Imported(Due, timeSpecified: true);

        UpdateDue(v, Due);
        v.DueTimeSpecified.Should().BeTrue("yalnız not değişti");

        UpdateDue(v, Due.AddDays(1));
        v.DueTimeSpecified.Should().BeFalse("bildirilmeyen yeni vade saatsiz sayılır");

        UpdateDue(v, Due);
        v.DueTimeSpecified.Should().BeFalse("bayrak kendiliğinden geri gelmez");
    }

    [Fact]
    public void Update_with_flag_marks_time_and_clearing_due_drops_it()
    {
        var v = Created(Due);

        UpdateDue(v, Due.AddHours(2), timeSpecified: true);
        v.DueTimeSpecified.Should().BeTrue("panelde saat girilerek değişti");

        UpdateDue(v, null, timeSpecified: true);
        v.DueTimeSpecified.Should().BeFalse("vadesiz kayıtta saat yoktur");
    }
}
