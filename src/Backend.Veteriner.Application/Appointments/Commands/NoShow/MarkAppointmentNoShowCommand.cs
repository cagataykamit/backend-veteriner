using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.NoShow;

/// <summary>Randevulu hasta gelmedi (<c>Appointments.NoShow</c>); idempotent. Gerekçe opsiyonel.</summary>
public sealed record MarkAppointmentNoShowCommand(Guid AppointmentId, string? Reason = null)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Appointment.NoShow";
    public string? AuditTarget => $"AppointmentId={AppointmentId}";
}
