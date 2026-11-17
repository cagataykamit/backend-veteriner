using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetCalendar;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Vaccinations.Calendar;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Queries.GetCalendar;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;
using Mediator;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Calendar;

/// <summary>Ay görünümü özeti (§7): takvim sorgusunu aynı filtrelerle gönderir, gün + durum bazında sayar; iptal ve gelmedi sayılmaz.</summary>
public sealed class CalendarSummaryTests
{
    // İstanbul 2026-11-16 12:00.
    private static readonly DateTime NowUtc = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Vet = Guid.NewGuid();

    private static AppointmentCalendarItemDto Appt(DateTime atUtc, AppointmentStatus status)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), atUtc, 30, atUtc.AddMinutes(30), status, AppointmentType.Checkup, "P", "C");

    [Fact]
    public void From_Groups_By_Day_And_State_Skipping_Stateless()
    {
        var d1 = new DateOnly(2026, 11, 16);
        var d2 = new DateOnly(2026, 11, 17);

        CalendarDaySummary.From([(d2, CalendarItemState.Upcoming), (d1, CalendarItemState.Overdue), (d1, CalendarItemState.Completed), (d1, null)])
            .Should().Equal(new CalendarDaySummaryDto(d1, 2, 0, 0, 1, 1), new CalendarDaySummaryDto(d2, 1, 0, 1, 0, 0));
    }

    [Fact]
    public async Task Appointments_Summary_Forwards_Filters_And_Counts_By_Istanbul_Day()
    {
        var sender = new Mock<ISender>();
        GetAppointmentsCalendarQuery? sent = null;
        sender.Setup(s => s.Send(It.IsAny<GetAppointmentsCalendarQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<IReadOnlyList<AppointmentCalendarItemDto>>>, CancellationToken>((q, _) => sent = (GetAppointmentsCalendarQuery)q)
            .ReturnsAsync(Result<IReadOnlyList<AppointmentCalendarItemDto>>.Success(new[]
            {
                Appt(new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc), AppointmentStatus.Scheduled), // İstanbul 16'sı 01:00 → bugün
                Appt(NowUtc.AddHours(1), AppointmentStatus.Completed),
                Appt(NowUtc.AddHours(2), AppointmentStatus.Cancelled),
                Appt(NowUtc.AddDays(1), AppointmentStatus.Scheduled),
            }));

        var result = await new GetAppointmentsCalendarSummaryQueryHandler(sender.Object, new FakeTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)))
            .Handle(new GetAppointmentsCalendarSummaryQuery(NowUtc, NowUtc.AddDays(30), VeterinarianUserId: Vet, States: [CalendarItemState.Upcoming]), CancellationToken.None);

        sent!.VeterinarianUserId.Should().Be(Vet);
        sent.States.Should().Equal(CalendarItemState.Upcoming);
        sent.Status.Should().BeNull();
        result.Value.Should().Equal(
            new CalendarDaySummaryDto(new DateOnly(2026, 11, 16), 2, 1, 0, 0, 1),
            new CalendarDaySummaryDto(new DateOnly(2026, 11, 17), 1, 0, 1, 0, 0));
    }

    [Fact]
    public async Task Appointments_Summary_Returns_Calendar_Failure()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetAppointmentsCalendarQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure("Appointments.ClinicScopeRequired", "x"));

        var result = await new GetAppointmentsCalendarSummaryQueryHandler(sender.Object, TimeProvider.System)
            .Handle(new GetAppointmentsCalendarSummaryQuery(NowUtc, NowUtc.AddDays(1)), CancellationToken.None);

        result.Error.Code.Should().Be("Appointments.ClinicScopeRequired");
    }

    [Fact]
    public async Task Vaccinations_Summary_Counts_Item_States()
    {
        var day = new DateOnly(2026, 11, 16);
        VaccinationCalendarItemDto V(CalendarItemState s) => new(Guid.NewGuid(), VaccinationCalendarKind.NextDue, s, day, NowUtc, Guid.NewGuid(), Guid.NewGuid(), "P", Guid.NewGuid(), "C", null, "Karma", null);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetVaccinationsCalendarQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<VaccinationCalendarItemDto>>.Success(new[] { V(CalendarItemState.Overdue), V(CalendarItemState.Overdue), V(CalendarItemState.Completed) }));

        var result = await new GetVaccinationsCalendarSummaryQueryHandler(sender.Object)
            .Handle(new GetVaccinationsCalendarSummaryQuery(NowUtc, NowUtc.AddDays(1)), CancellationToken.None);

        result.Value!.Single().Should().Be(new CalendarDaySummaryDto(day, 3, 0, 0, 2, 1));
    }
}
