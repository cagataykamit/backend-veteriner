using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Persistence.Entities;

namespace Backend.Veteriner.Infrastructure.Outbox;

internal static class OutboxMessageQueryFilters
{
    private static readonly string[] AppointmentIntegrationEventTypeValues =
        AppointmentIntegrationEventTypes.All.ToArray();

    private static readonly string[] ClientIntegrationEventTypeValues =
        ClientIntegrationEventTypes.All.ToArray();

    private static readonly string[] PetIntegrationEventTypeValues =
        PetIntegrationEventTypes.All.ToArray();

    // Projection (read-model) integration event tipleri: generic OutboxProcessor bunları tüketmez;
    // her birinin kendi dedike projection processor'ı (appointment / client / pet) vardır.
    private static readonly string[] ProjectionIntegrationEventTypeValues =
    [
        .. AppointmentIntegrationEventTypeValues,
        .. ClientIntegrationEventTypeValues,
        .. PetIntegrationEventTypeValues
    ];

    public static IQueryable<OutboxMessage> ExcludingAppointmentIntegrationEvents(IQueryable<OutboxMessage> query)
        => query.Where(m => !AppointmentIntegrationEventTypeValues.Contains(m.Type));

    public static IQueryable<OutboxMessage> AppointmentIntegrationEventsOnly(IQueryable<OutboxMessage> query)
        => query.Where(m => AppointmentIntegrationEventTypeValues.Contains(m.Type));

    public static IQueryable<OutboxMessage> ClientIntegrationEventsOnly(IQueryable<OutboxMessage> query)
        => query.Where(m => ClientIntegrationEventTypeValues.Contains(m.Type));

    public static IQueryable<OutboxMessage> PetIntegrationEventsOnly(IQueryable<OutboxMessage> query)
        => query.Where(m => PetIntegrationEventTypeValues.Contains(m.Type));

    public static IQueryable<OutboxMessage> ExcludingProjectionIntegrationEvents(IQueryable<OutboxMessage> query)
        => query.Where(m => !ProjectionIntegrationEventTypeValues.Contains(m.Type));

    public static bool IsProjectionIntegrationEvent(string type)
        => AppointmentIntegrationEventTypes.IsKnown(type)
           || ClientIntegrationEventTypes.IsKnown(type)
           || PetIntegrationEventTypes.IsKnown(type);
}
