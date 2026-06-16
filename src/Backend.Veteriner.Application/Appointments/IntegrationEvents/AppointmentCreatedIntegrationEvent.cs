namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

public sealed record AppointmentCreatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    AppointmentProjectionSnapshot Current);
