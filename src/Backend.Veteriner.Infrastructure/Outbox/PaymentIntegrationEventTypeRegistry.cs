using Backend.Veteriner.Application.Payments.IntegrationEvents;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// OutboxMessages.Type → payment integration event payload CLR tipi eşlemesi.
/// </summary>
public sealed class PaymentIntegrationEventTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Type> PayloadTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [PaymentIntegrationEventTypes.Created] = typeof(PaymentCreatedIntegrationEvent),
            [PaymentIntegrationEventTypes.Updated] = typeof(PaymentUpdatedIntegrationEvent),
        };

    public static bool TryResolvePayloadType(string eventType, out Type payloadType)
        => PayloadTypes.TryGetValue(eventType, out payloadType!);

    public static Type ResolvePayloadType(string eventType)
    {
        if (TryResolvePayloadType(eventType, out var payloadType))
            return payloadType;

        throw new UnknownPaymentIntegrationEventTypeException(eventType);
    }
}
