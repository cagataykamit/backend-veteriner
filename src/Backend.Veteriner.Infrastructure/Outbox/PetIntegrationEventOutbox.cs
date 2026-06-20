using System.Text.Json;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Common.Outbox;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Pet integration event'lerini JSON olarak <see cref="IOutboxBuffer"/>'a aktarır.
/// SaveChanges veya transaction başlatmaz; buffer aynı SaveChanges içinde drain edilir.
/// </summary>
public sealed class PetIntegrationEventOutbox : IPetIntegrationEventOutbox
{
    private readonly IOutboxBuffer _buffer;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PetIntegrationEventOutbox(IOutboxBuffer buffer) => _buffer = buffer;

    public async Task EnqueueAsync<TEvent>(
        string eventType,
        TEvent integrationEvent,
        CancellationToken ct = default)
        where TEvent : notnull
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (eventType.Length > PetIntegrationEventTypes.MaxTypeLength)
        {
            throw new ArgumentException(
                $"Event type exceeds {PetIntegrationEventTypes.MaxTypeLength} characters.",
                nameof(eventType));
        }

        if (!PetIntegrationEventTypes.IsKnown(eventType))
            throw new UnknownPetIntegrationEventTypeException(eventType);

        var expectedPayloadType = PetIntegrationEventTypeRegistry.ResolvePayloadType(eventType);
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
