using Backend.Veteriner.Application.Vaccinations.Calendar;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Calendar;

/// <summary>Takvim aşı katmanı: renk durumu İstanbul gününe göre (ADR-028), sonraki tarihin karşılanması aynı aşının yeniden uygulanmasıyla.</summary>
public sealed class VaccinationCalendarRulesTests
{
    // 2026-11-16 09:00 UTC = İstanbul 12:00; bugünün başı 2026-11-15 21:00 UTC, yarının başı 2026-11-16 21:00 UTC.
    private static readonly DateTime NowUtc = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayStartUtc = new(2026, 11, 15, 21, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-1, VaccinationCalendarState.Overdue)]
    [InlineData(0, VaccinationCalendarState.DueToday)]
    [InlineData(23 * 60 + 59, VaccinationCalendarState.DueToday)]
    [InlineData(24 * 60, VaccinationCalendarState.Upcoming)]
    public void DueState_Should_Use_IstanbulDay(int minutesFromTodayStart, VaccinationCalendarState expected)
        => VaccinationCalendarRules.DueState(TodayStartUtc.AddMinutes(minutesFromTodayStart), NowUtc).Should().Be(expected);

    [Fact]
    public void DueState_Should_Keep_TodayDue_As_Today_After_Its_Time_Passed()
        => VaccinationCalendarRules.DueState(NowUtc.AddHours(-2), NowUtc).Should().Be(VaccinationCalendarState.DueToday);

    [Fact]
    public void LocalDate_Should_Be_IstanbulDate()
        => VaccinationCalendarRules.LocalDate(TodayStartUtc).Should().Be(new DateOnly(2026, 11, 16));

    [Fact]
    public void NextDue_Should_Be_Fulfilled_By_Later_Dose_Of_Same_Definition()
    {
        var petId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var source = new VaccinationDose(Guid.NewGuid(), petId, definitionId, "Karma", NowUtc.AddYears(-1));

        VaccinationCalendarRules.IsNextDueFulfilled(source, [new(Guid.NewGuid(), petId, definitionId, "Karma (yeni ad)", NowUtc.AddDays(-3))])
            .Should().BeTrue();
    }

    [Fact]
    public void NextDue_Should_Stay_Open_For_Other_Vaccine_Other_Pet_Or_Earlier_Dose()
    {
        var petId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var source = new VaccinationDose(Guid.NewGuid(), petId, definitionId, "Karma", NowUtc.AddYears(-1));

        VaccinationCalendarRules.IsNextDueFulfilled(source,
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

        VaccinationCalendarRules.IsNextDueFulfilled(source, [new(Guid.NewGuid(), petId, null, " iç parazit ", NowUtc.AddDays(-1))])
            .Should().BeTrue();
    }
}
