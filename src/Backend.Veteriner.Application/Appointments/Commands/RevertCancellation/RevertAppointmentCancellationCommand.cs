using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Commands.RevertCancellation;

/// <summary>İptali kısa süre içinde geri alır (<c>Cancelled → Scheduled</c>, "Geri al"); audit'li. Süre <c>Appointment.CancellationUndoLimit</c>.</summary>
public sealed record RevertAppointmentCancellationCommand(Guid AppointmentId)
    : IRequest<Result>, IAuditableRequest, ITransactionalRequest
{
    public string AuditAction => "Appointment.CancelRevert";
    public string? AuditTarget => $"AppointmentId={AppointmentId}";
}
