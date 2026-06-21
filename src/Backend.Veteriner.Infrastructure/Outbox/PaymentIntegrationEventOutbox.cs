using System.Text.Json;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Payments.IntegrationEvents;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Payment integration event'lerini JSON olarak <see cref="IOutboxBuffer"/>'a aktarır.
/// SaveChanges veya transaction başlatmaz; buffer aynı SaveChanges içinde drain edilir.
/// </summary>
public sealed class PaymentIntegrationEventOutbox : IPaymentIntegrationEventOutbox
{
    private readonly IOutboxBuffer _buffer;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PaymentIntegrationEventOutbox(IOutboxBuffer buffer) => _buffer = buffer;

    public async Task EnqueueAsync<TEvent>(
        string eventType,
        TEvent integrationEvent,
        CancellationToken ct = default)
        where TEvent : notnull
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (eventType.Length > PaymentIntegrationEventTypes.MaxTypeLength)
        {
            throw new ArgumentException(
                $"Event type exceeds {PaymentIntegrationEventTypes.MaxTypeLength} characters.",
                nameof(eventType));
        }

        if (!PaymentIntegrationEventTypes.IsKnown(eventType))
            throw new UnknownPaymentIntegrationEventTypeException(eventType);

        var expectedPayloadType = PaymentIntegrationEventTypeRegistry.ResolvePayloadType(eventType);
        if (expectedPayloadType != typeof(TEvent))
        {
            throw new ArgumentException(
                $"Payload type '{typeof(TEvent).Name}' does not match event type '{eventType}' (expected '{expectedPayloadType.Name}').",
                nameof(integrationEvent));
        }

        var payload = JsonSerializer.Serialize(integrationEvent, typeof(TEvent), JsonOptions);
        await _buffer.EnqueueAsync(eventType, payload, ct);
    }
}
