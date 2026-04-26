using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Queries.GetCalendar;

public sealed record GetAppointmentsCalendarQuery(
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    Guid? ClinicId = null,
    AppointmentStatus? Status = null)
    : IRequest<Result<IReadOnlyList<AppointmentCalendarItemDto>>>;
