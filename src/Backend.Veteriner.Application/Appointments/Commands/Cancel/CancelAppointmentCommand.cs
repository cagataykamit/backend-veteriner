using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Commands.Cancel;

public sealed record CancelAppointmentCommand(Guid AppointmentId, string? Reason = null)
    : IRequest<Result<CancelAppointmentResultDto>>, IAuditableRequest
{
    public string AuditAction => "Appointment.Cancel";
    public string? AuditTarget => $"AppointmentId={AppointmentId}";
}
