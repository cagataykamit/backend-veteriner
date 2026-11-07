using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Vaccinations;

/// <summary>TIER-005: takvim satırı sınırları ve kalan doz hesabı (temel seri, tekrar, geç kalan doz, doğum tarihi yok).</summary>
public sealed class VaccinationScheduleItemTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Earliest = new(2026, 11, 9);

    private static VaccinationScheduleItem New(int startAgeWeeks = 6, int doseCount = 3, int? doseIntervalWeeks = 3, int? repeatIntervalMonths = 12)
    {
        var result = VaccinationScheduleItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startAgeWeeks, doseCount, doseIntervalWeeks, repeatIntervalMonths, null, Now);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    [Theory]
    [InlineData(-1, 1, null, null)]
    [InlineData(521, 1, null, null)]
    [InlineData(6, 0, null, null)]
    [InlineData(6, 6, 3, null)]
    [InlineData(6, 2, null, null)]
    [InlineData(6, 2, 53, null)]
    [InlineData(6, 1, null, 0)]
    [InlineData(6, 1, null, 61)]
    public void Create_Should_Reject_Out_Of_Range_Values(int startAge, int doseCount, int? interval, int? repeat)
    {
        var result = VaccinationScheduleItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startAge, doseCount, interval, repeat, null, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(VaccinationScheduleItem.ValidationCode);
    }

    [Fact]
    public void Single_Dose_Should_Drop_Interval_And_Trim_Notes()
    {
        var item = VaccinationScheduleItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12, 1, 4, 12, "  yıllık  ", Now).Value!;

        item.DoseIntervalWeeks.Should().BeNull();
        item.Notes.Should().Be("yıllık");
    }

    [Fact]
    public void Update_Should_Change_Nothing_When_Invalid()
    {
        var item = New();

        item.Update(6, 0, 3, 12, null, Now).IsSuccess.Should().BeFalse();

        item.DoseCount.Should().Be(3);
        item.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Puppy_Without_Doses_Should_Get_Full_Series_From_Birth_And_First_Repeat()
    {
        var item = New();
        var birth = new DateOnly(2026, 10, 20);

        var doses = item.PlanDoses(birth, [], Earliest);

        doses.Should().Equal(
            new PlannedVaccinationDose(1, new DateOnly(2026, 12, 1), false),
            new PlannedVaccinationDose(2, new DateOnly(2026, 12, 22), false),
            new PlannedVaccinationDose(3, new DateOnly(2027, 1, 12), false),
            new PlannedVaccinationDose(null, new DateOnly(2028, 1, 12), false));
    }

    [Fact]
    public void Partial_Series_Should_Continue_From_Last_Applied_Dose()
    {
        var item = New();

        var doses = item.PlanDoses(new DateOnly(2026, 9, 1), [new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 27)], Earliest);

        doses.Should().Equal(
            new PlannedVaccinationDose(3, new DateOnly(2026, 11, 17), false),
            new PlannedVaccinationDose(null, new DateOnly(2027, 11, 17), false));
    }

    [Fact]
    public void Late_First_Dose_Should_Move_To_Earliest_And_Shift_The_Rest()
    {
        var item = New();

        var doses = item.PlanDoses(new DateOnly(2024, 1, 1), [], Earliest);

        doses[0].Should().Be(new PlannedVaccinationDose(1, Earliest, true));
        doses[1].Should().Be(new PlannedVaccinationDose(2, Earliest.AddDays(21), false));
    }

    [Fact]
    public void Missing_Birth_Date_Should_Start_At_Earliest_Without_Late_Flag()
    {
        var doses = New(doseCount: 1, doseIntervalWeeks: null).PlanDoses(null, [], Earliest);

        doses.Should().Equal(
            new PlannedVaccinationDose(1, Earliest, false),
            new PlannedVaccinationDose(null, Earliest.AddMonths(12), false));
    }

    [Fact]
    public void Completed_Series_Should_Suggest_Only_Next_Repeat()
    {
        var doses = New(doseCount: 1, doseIntervalWeeks: null).PlanDoses(null, [new DateOnly(2026, 3, 1), new DateOnly(2025, 3, 1)], Earliest);

        doses.Should().Equal(new PlannedVaccinationDose(null, new DateOnly(2027, 3, 1), false));
    }

    [Fact]
    public void Completed_Series_Without_Repeat_Should_Suggest_Nothing()
        => New(repeatIntervalMonths: null).PlanDoses(null, [Earliest.AddDays(-60), Earliest.AddDays(-40), Earliest.AddDays(-20)], Earliest)
            .Should().BeEmpty();
}
