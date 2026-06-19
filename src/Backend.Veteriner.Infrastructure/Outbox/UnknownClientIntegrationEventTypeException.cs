namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Bilinmeyen client integration event type için fırlatılır.
/// </summary>
public sealed class UnknownClientIntegrationEventTypeException : Exception
{
    public UnknownClientIntegrationEventTypeException(string eventType)
        : base($"Unknown client integration event type: '{eventType}'.")
    {
        EventType = eventType;
    }

    public string EventType { get; }
}
