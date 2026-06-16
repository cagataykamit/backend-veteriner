namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

public sealed record AppointmentUpdatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    AppointmentProjectionSnapshot Previous,
    AppointmentProjectionSnapshot Current);
