using Backend.Veteriner.Application.Appointments.Calendar;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments.Calendar;

/// <summary>Takvim filtresi (§6): hekim, hekimsiz, tür ve durum; durum dili aşı katmanıyla aynı (İstanbul günü).</summary>
public sealed class AppointmentCalendarFilterTests
{
    // İstanbul 2026-11-16 12:00; bugünün başı 2026-11-15 21:00 UTC.
    private static readonly DateTime NowUtc = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Vet = Guid.NewGuid();

    private static AppointmentCalendarFilter F(Guid? vet = null, bool none = false, AppointmentType[]? types = null, CalendarItemState[]? states = null)
        => new(vet, none, types, states);

    [Fact]
    public void Empty_Filter_Passes_Everything()
    {
        F().IsEmpty.Should().BeTrue();
        F().Matches(null, AppointmentType.Other, AppointmentStatus.Cancelled, NowUtc, NowUtc).Should().BeTrue();
    }

    [Fact]
    public void Veterinarian_And_Unassigned()
    {
        F(vet: Vet).Matches(Vet, AppointmentType.Checkup, AppointmentStatus.Scheduled, NowUtc, NowUtc).Should().BeTrue();
        F(vet: Vet).Matches(Guid.NewGuid(), AppointmentType.Checkup, AppointmentStatus.Scheduled, NowUtc, NowUtc).Should().BeFalse();
        F(vet: Vet).Matches(null, AppointmentType.Checkup, AppointmentStatus.Scheduled, NowUtc, NowUtc).Should().BeFalse();
        F(none: true).Matches(null, AppointmentType.Checkup, AppointmentStatus.Scheduled, NowUtc, NowUtc).Should().BeTrue();
        F(none: true).Matches(Vet, AppointmentType.Checkup, AppointmentStatus.Scheduled, NowUtc, NowUtc).Should().BeFalse();
    }

    [Fact]
    public void Types()
    {
        var f = F(types: [AppointmentType.Vaccination, AppointmentType.Checkup]);
        f.Matches(null, AppointmentType.Vaccination, AppointmentStatus.Scheduled, NowUtc, NowUtc).Should().BeTrue();
        f.Matches(null, AppointmentType.Surgery, AppointmentStatus.Scheduled, NowUtc, NowUtc).Should().BeFalse();
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled, -30, CalendarItemState.Overdue)]
    [InlineData(AppointmentStatus.Scheduled, -1, CalendarItemState.DueToday)]
    [InlineData(AppointmentStatus.Scheduled, 2, CalendarItemState.DueToday)]
    [InlineData(AppointmentStatus.Scheduled, 24, CalendarItemState.Upcoming)]
    [InlineData(AppointmentStatus.Completed, -30, CalendarItemState.Completed)]
    public void State_Is_Day_Based(AppointmentStatus status, int hoursFromNow, CalendarItemState expected)
    {
        CalendarItemStateRule.ForAppointment(status, NowUtc.AddHours(hoursFromNow), NowUtc).Should().Be(expected);
        F(states: [expected]).Matches(null, AppointmentType.Checkup, status, NowUtc.AddHours(hoursFromNow), NowUtc).Should().BeTrue();
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Cancelled_And_NoShow_Have_No_State(AppointmentStatus status)
    {
        CalendarItemStateRule.ForAppointment(status, NowUtc, NowUtc).Should().BeNull();
        F(states: [CalendarItemState.DueToday, CalendarItemState.Completed]).Matches(null, AppointmentType.Checkup, status, NowUtc, NowUtc).Should().BeFalse();
    }

    [Fact]
    public void Filters_Combine_With_And()
        => F(vet: Vet, types: [AppointmentType.Checkup], states: [CalendarItemState.Upcoming])
            .Matches(Vet, AppointmentType.Checkup, AppointmentStatus.Scheduled, NowUtc, NowUtc)
            .Should().BeFalse();
}
