namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

public sealed record AppointmentCreatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    long AppointmentSequence,
    AppointmentProjectionSnapshot Current) : IAppointmentOrderedIntegrationEvent
{
    public Guid AppointmentId => Current.AppointmentId;
}
