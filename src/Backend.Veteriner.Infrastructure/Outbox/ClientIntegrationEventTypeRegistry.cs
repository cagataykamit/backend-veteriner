using Backend.Veteriner.Application.Clients.IntegrationEvents;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// OutboxMessages.Type → client integration event payload CLR tipi eşlemesi.
/// Reflection veya assembly taraması kullanmaz; ileride Client projection processor tarafından da tüketilebilir.
/// </summary>
public sealed class ClientIntegrationEventTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Type> PayloadTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [ClientIntegrationEventTypes.Created] = typeof(ClientCreatedIntegrationEvent),
            [ClientIntegrationEventTypes.Updated] = typeof(ClientUpdatedIntegrationEvent),
        };

    public static bool TryResolvePayloadType(string eventType, out Type payloadType)
        => PayloadTypes.TryGetValue(eventType, out payloadType!);

    public static Type ResolvePayloadType(string eventType)
    {
        if (TryResolvePayloadType(eventType, out var payloadType))
            return payloadType;

        throw new UnknownClientIntegrationEventTypeException(eventType);
    }
}
