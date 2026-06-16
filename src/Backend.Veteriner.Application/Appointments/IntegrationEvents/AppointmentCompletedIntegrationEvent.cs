namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

public sealed record AppointmentCompletedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    AppointmentProjectionSnapshot Previous,
    AppointmentProjectionSnapshot Current);
