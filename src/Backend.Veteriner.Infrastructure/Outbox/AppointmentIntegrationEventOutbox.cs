using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Outbox;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Appointment integration event'lerini JSON olarak <see cref="IOutboxBuffer"/>'a aktarır.
/// SaveChanges veya transaction başlatmaz.
/// </summary>
public sealed class AppointmentIntegrationEventOutbox : IAppointmentIntegrationEventOutbox
{
    private readonly IOutboxBuffer _buffer;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AppointmentIntegrationEventOutbox(IOutboxBuffer buffer) => _buffer = buffer;

    public async Task EnqueueAsync<TEvent>(
        string eventType,
        TEvent integrationEvent,
        CancellationToken ct = default)
        where TEvent : notnull
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (eventType.Length > AppointmentIntegrationEventTypes.MaxTypeLength)
        {
            throw new ArgumentException(
                $"Event type exceeds {AppointmentIntegrationEventTypes.MaxTypeLength} characters.",
                nameof(eventType));
        }

        if (!AppointmentIntegrationEventTypes.IsKnown(eventType))
            throw new UnknownAppointmentIntegrationEventTypeException(eventType);

        var expectedPayloadType = AppointmentIntegrationEventTypeRegistry.ResolvePayloadType(eventType);
        if (expectedPayloadType != typeof(TEvent))
        {
            throw new ArgumentException(
                $"Payload type '{typeof(TEvent).Name}' does not match event type '{eventType}' (expected '{expectedPayloadType.Name}').",
                nameof(integrationEvent));
        }

        Guid? appointmentId = null;
        long? appointmentSequence = null;
        if (integrationEvent is IAppointmentOrderedIntegrationEvent ordered)
        {
            if (ordered.AppointmentId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Appointment integration events require a non-empty AppointmentId.",
                    nameof(integrationEvent));
            }

            if (ordered.AppointmentSequence <= 0)
            {
                throw new ArgumentException(
                    "Appointment integration events require AppointmentSequence > 0.",
                    nameof(integrationEvent));
            }

            appointmentId = ordered.AppointmentId;
            appointmentSequence = ordered.AppointmentSequence;
        }

        var payload = JsonSerializer.Serialize(integrationEvent, typeof(TEvent), JsonOptions);
        await _buffer.EnqueueAsync(eventType, payload, ct, appointmentId, appointmentSequence);
    }
}
