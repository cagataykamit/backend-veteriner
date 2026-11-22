using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.Vaccinations.Calendar;
using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Calendar;

/// <summary>Takvim aşı katmanı: renk durumu İstanbul gününe göre (ADR-028), sonraki tarihin karşılanması aynı aşının yeniden uygulanmasıyla.</summary>
public sealed class VaccinationCalendarRulesTests
{
    // 2026-11-16 09:00 UTC = İstanbul 12:00; bugünün başı 2026-11-15 21:00 UTC, yarının başı 2026-11-16 21:00 UTC.
    private static readonly DateTime NowUtc = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayStartUtc = new(2026, 11, 15, 21, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-1, CalendarItemState.Overdue)]
    [InlineData(0, CalendarItemState.DueToday)]
    [InlineData(23 * 60 + 59, CalendarItemState.DueToday)]
    [InlineData(24 * 60, CalendarItemState.Upcoming)]
    public void DueState_Should_Use_IstanbulDay(int minutesFromTodayStart, CalendarItemState expected)
        => CalendarItemStateRule.ForDue(TodayStartUtc.AddMinutes(minutesFromTodayStart), NowUtc).Should().Be(expected);

    [Fact]
    public void DueState_Should_Keep_TodayDue_As_Today_After_Its_Time_Passed()
        => CalendarItemStateRule.ForDue(NowUtc.AddHours(-2), NowUtc).Should().Be(CalendarItemState.DueToday);

    [Fact]
    public void Appointment_Overdue_State_Should_Have_No_Lower_Bound()
        => CalendarItemStateRule.ForAppointment(AppointmentStatus.Scheduled, TodayStartUtc.AddYears(-2), NowUtc)
            .Should().Be(CalendarItemState.Overdue);

    // 2026-11-21 kararı: bugün İstanbul 2026-11-16 → gecikmiş penceresi başı İstanbul 2026-10-16 00:00 (2026-10-15 21:00 UTC).
    [Theory]
    [InlineData(VaccinationCalendarKind.Planned, 0, true)] // tam 1 ay önce
    [InlineData(VaccinationCalendarKind.Planned, -1, false)] // bir dakika daha eski
    [InlineData(VaccinationCalendarKind.NextDue, 0, true)]
    [InlineData(VaccinationCalendarKind.NextDue, -1, false)]
    [InlineData(VaccinationCalendarKind.Applied, -1, true)] // uygulanan aşı geçmişi pencereye girmez
    public void Due_Rows_Older_Than_Overdue_Window_Should_Not_Be_Shown(VaccinationCalendarKind kind, int minutesFromWindowStart, bool expected)
    {
        var windowStartUtc = new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);

        VaccinationCalendarRules.IsShown(kind, windowStartUtc.AddMinutes(minutesFromWindowStart), VaccinationDueRule.OverdueWindow(NowUtc))
            .Should().Be(expected);
    }

    [Fact]
    public void LocalDate_Should_Be_IstanbulDate()
        => VaccinationCalendarRules.LocalDate(TodayStartUtc).Should().Be(new DateOnly(2026, 11, 16));

    [Fact]
    public void NextDue_Should_Be_Fulfilled_By_Later_Dose_Of_Same_Definition()
    {
        var petId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var source = new VaccinationDose(Guid.NewGuid(), petId, definitionId, "Karma", NowUtc.AddYears(-1));

        VaccinationDueRule.IsNextDueFulfilled(source, [new(Guid.NewGuid(), petId, definitionId, "Karma (yeni ad)", NowUtc.AddDays(-3))])
            .Should().BeTrue();
    }

    [Fact]
    public void NextDue_Should_Stay_Open_For_Other_Vaccine_Other_Pet_Or_Earlier_Dose()
    {
        var petId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var source = new VaccinationDose(Guid.NewGuid(), petId, definitionId, "Karma", NowUtc.AddYears(-1));

        VaccinationDueRule.IsNextDueFulfilled(source,
        [
            new(Guid.NewGuid(), petId, Guid.NewGuid(), "Kuduz", NowUtc.AddDays(-3)),
            new(Guid.NewGuid(), Guid.NewGuid(), definitionId, "Karma", NowUtc.AddDays(-3)),
            new(Guid.NewGuid(), petId, definitionId, "Karma", NowUtc.AddYears(-2)),
            source,
        ]).Should().BeFalse();
    }

    [Fact]
    public void NextDue_Without_Definition_Should_Match_By_Name_IgnoringCase()
    {
        var petId = Guid.NewGuid();
        var source = new VaccinationDose(Guid.NewGuid(), petId, null, "İç Parazit", NowUtc.AddMonths(-3));

        VaccinationDueRule.IsNextDueFulfilled(source, [new(Guid.NewGuid(), petId, null, " iç parazit ", NowUtc.AddDays(-1))])
            .Should().BeTrue();
    }
}
