using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.RevertNoShow;

/// <summary>Gelmedi işaretini gerekçeyle geri alır (<c>Appointments.NoShow</c>); gerekçe zorunlu, audit'li.</summary>
public sealed record RevertAppointmentNoShowCommand(Guid AppointmentId, string Reason)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Appointment.NoShowRevert";
    public string? AuditTarget => $"AppointmentId={AppointmentId}";
}
