namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

public sealed record AppointmentRescheduledIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    AppointmentProjectionSnapshot Previous,
    AppointmentProjectionSnapshot Current);
