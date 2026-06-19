namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

public sealed record AppointmentUpdatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    long AppointmentSequence,
    AppointmentProjectionSnapshot Previous,
    AppointmentProjectionSnapshot Current) : IAppointmentOrderedIntegrationEvent
{
    public Guid AppointmentId => Current.AppointmentId;
}
