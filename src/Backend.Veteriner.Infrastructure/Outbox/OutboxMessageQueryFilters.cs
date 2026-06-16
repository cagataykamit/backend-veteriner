using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Persistence.Entities;

namespace Backend.Veteriner.Infrastructure.Outbox;

internal static class OutboxMessageQueryFilters
{
    private static readonly string[] AppointmentIntegrationEventTypeValues =
        AppointmentIntegrationEventTypes.All.ToArray();

    public static IQueryable<OutboxMessage> ExcludingAppointmentIntegrationEvents(IQueryable<OutboxMessage> query)
        => query.Where(m => !AppointmentIntegrationEventTypeValues.Contains(m.Type));

    public static IQueryable<OutboxMessage> AppointmentIntegrationEventsOnly(IQueryable<OutboxMessage> query)
        => query.Where(m => AppointmentIntegrationEventTypeValues.Contains(m.Type));
}
