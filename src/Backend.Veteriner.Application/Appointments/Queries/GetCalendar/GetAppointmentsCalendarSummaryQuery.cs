using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Queries.GetCalendar;

/// <summary>
/// Takvim ay görünümü: <see cref="GetAppointmentsCalendarQuery"/> ile aynı aralık, kapsam ve filtreler; gün başına sayı döner (§7).
/// </summary>
public sealed record GetAppointmentsCalendarSummaryQuery(
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    Guid? ClinicId = null,
    Guid? VeterinarianUserId = null,
    bool WithoutVeterinarian = false,
    IReadOnlyList<AppointmentType>? AppointmentTypes = null,
    IReadOnlyList<CalendarItemState>? States = null)
    : IRequest<Result<IReadOnlyList<CalendarDaySummaryDto>>>;

/// <summary>Kural tek yerde kalsın diye takvim sorgusunu gönderir (doğrulama, kapsam ve filtre orada) ve gün + durum bazında sayar.</summary>
public sealed class GetAppointmentsCalendarSummaryQueryHandler
    : IRequestHandler<GetAppointmentsCalendarSummaryQuery, Result<IReadOnlyList<CalendarDaySummaryDto>>>
{
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public GetAppointmentsCalendarSummaryQueryHandler(ISender sender, TimeProvider timeProvider)
    {
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<IReadOnlyList<CalendarDaySummaryDto>>> Handle(GetAppointmentsCalendarSummaryQuery request, CancellationToken ct)
    {
        var items = await _sender.Send(
            new GetAppointmentsCalendarQuery(
                request.DateFromUtc,
                request.DateToUtc,
                request.ClinicId,
                Status: null,
                request.VeterinarianUserId,
                request.WithoutVeterinarian,
                request.AppointmentTypes,
                request.States),
            ct);
        if (!items.IsSuccess)
            return Result<IReadOnlyList<CalendarDaySummaryDto>>.Failure(items.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        return Result<IReadOnlyList<CalendarDaySummaryDto>>.Success(CalendarDaySummary.From(
            items.Value!.Select(i => (OperationDayBounds.ToLocalDate(i.ScheduledAtUtc), CalendarItemStateRule.ForAppointment(i.Status, i.ScheduledAtUtc, utcNow)))));
    }
}
