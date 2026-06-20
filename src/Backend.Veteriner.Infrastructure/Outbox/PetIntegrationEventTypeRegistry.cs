using Backend.Veteriner.Application.Pets.IntegrationEvents;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// OutboxMessages.Type → pet integration event payload CLR tipi eşlemesi.
/// Reflection veya assembly taraması kullanmaz; ileride Pet projection processor tarafından da tüketilebilir.
/// </summary>
public sealed class PetIntegrationEventTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Type> PayloadTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [PetIntegrationEventTypes.Created] = typeof(PetCreatedIntegrationEvent),
            [PetIntegrationEventTypes.Updated] = typeof(PetUpdatedIntegrationEvent),
        };

    public static bool TryResolvePayloadType(string eventType, out Type payloadType)
        => PayloadTypes.TryGetValue(eventType, out payloadType!);

    public static Type ResolvePayloadType(string eventType)
    {
        if (TryResolvePayloadType(eventType, out var payloadType))
            return payloadType;

        throw new UnknownPetIntegrationEventTypeException(eventType);
    }
}
