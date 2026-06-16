namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

public sealed record AppointmentCancelledIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    AppointmentProjectionSnapshot Previous,
    AppointmentProjectionSnapshot Current);
