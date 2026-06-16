using Backend.Veteriner.Application.Appointments.IntegrationEvents;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// OutboxMessages.Type → appointment integration event payload CLR tipi eşlemesi.
/// Reflection veya assembly taraması kullanmaz; projector tarafından da tüketilebilir.
/// </summary>
public sealed class AppointmentIntegrationEventTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Type> PayloadTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [AppointmentIntegrationEventTypes.Created] = typeof(AppointmentCreatedIntegrationEvent),
            [AppointmentIntegrationEventTypes.Updated] = typeof(AppointmentUpdatedIntegrationEvent),
            [AppointmentIntegrationEventTypes.Rescheduled] = typeof(AppointmentRescheduledIntegrationEvent),
            [AppointmentIntegrationEventTypes.Cancelled] = typeof(AppointmentCancelledIntegrationEvent),
            [AppointmentIntegrationEventTypes.Completed] = typeof(AppointmentCompletedIntegrationEvent),
        };

    public static bool TryResolvePayloadType(string eventType, out Type payloadType)
        => PayloadTypes.TryGetValue(eventType, out payloadType!);

    public static Type ResolvePayloadType(string eventType)
    {
        if (TryResolvePayloadType(eventType, out var payloadType))
            return payloadType;

        throw new UnknownAppointmentIntegrationEventTypeException(eventType);
    }
}
