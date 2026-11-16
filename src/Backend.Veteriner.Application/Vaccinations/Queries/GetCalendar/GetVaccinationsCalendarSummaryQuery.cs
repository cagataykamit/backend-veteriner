using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetCalendar;

/// <summary>Takvim ay görünümünde aşı katmanı: <see cref="GetVaccinationsCalendarQuery"/> ile aynı kurallar, gün başına sayı (§7).</summary>
public sealed record GetVaccinationsCalendarSummaryQuery(
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    Guid? ClinicId = null,
    IReadOnlyList<CalendarItemState>? States = null)
    : IRequest<Result<IReadOnlyList<CalendarDaySummaryDto>>>;

public sealed class GetVaccinationsCalendarSummaryQueryHandler
    : IRequestHandler<GetVaccinationsCalendarSummaryQuery, Result<IReadOnlyList<CalendarDaySummaryDto>>>
{
    private readonly ISender _sender;

    public GetVaccinationsCalendarSummaryQueryHandler(ISender sender) => _sender = sender;

    public async ValueTask<Result<IReadOnlyList<CalendarDaySummaryDto>>> Handle(GetVaccinationsCalendarSummaryQuery request, CancellationToken ct)
    {
        var items = await _sender.Send(new GetVaccinationsCalendarQuery(request.DateFromUtc, request.DateToUtc, request.ClinicId, request.States), ct);
        if (!items.IsSuccess)
            return Result<IReadOnlyList<CalendarDaySummaryDto>>.Failure(items.Error);

        return Result<IReadOnlyList<CalendarDaySummaryDto>>.Success(
            CalendarDaySummary.From(items.Value!.Select(i => (i.Date, (CalendarItemState?)i.State))));
    }
}
