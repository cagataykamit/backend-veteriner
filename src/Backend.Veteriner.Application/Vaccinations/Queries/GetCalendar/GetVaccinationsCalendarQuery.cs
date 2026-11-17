using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetCalendar;

/// <summary>Randevu takviminin aşı katmanı (APPOINTMENT_CALENDAR_API_CONTRACT.md §5); aralık randevu takvimiyle aynı, en çok 45 gün. <c>States</c> boşsa tüm durumlar.</summary>
public sealed record GetVaccinationsCalendarQuery(
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    Guid? ClinicId = null,
    IReadOnlyList<CalendarItemState>? States = null)
    : IRequest<Result<IReadOnlyList<VaccinationCalendarItemDto>>>;
