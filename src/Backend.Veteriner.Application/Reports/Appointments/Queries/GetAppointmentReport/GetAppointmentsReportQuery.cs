using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.GetAppointmentReport;

/// <summary><c>from</c>/<c>to</c>: UTC; filtre <see cref="Appointment.ScheduledAtUtc"/> <c>[from,to]</c> dahil.</summary>
public sealed record GetAppointmentsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    AppointmentStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search,
    int Page,
    int PageSize) : IRequest<Result<AppointmentReportResultDto>>;
