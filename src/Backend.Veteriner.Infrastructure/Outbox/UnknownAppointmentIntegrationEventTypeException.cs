namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Bilinmeyen appointment integration event type için fırlatılır.
/// </summary>
public sealed class UnknownAppointmentIntegrationEventTypeException : Exception
{
    public UnknownAppointmentIntegrationEventTypeException(string eventType)
        : base($"Unknown appointment integration event type: '{eventType}'.")
    {
        EventType = eventType;
    }

    public string EventType { get; }
}
