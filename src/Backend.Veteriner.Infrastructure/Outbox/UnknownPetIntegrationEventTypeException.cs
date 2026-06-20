namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Bilinmeyen pet integration event type için fırlatılır.
/// </summary>
public sealed class UnknownPetIntegrationEventTypeException : Exception
{
    public UnknownPetIntegrationEventTypeException(string eventType)
        : base($"Unknown pet integration event type: '{eventType}'.")
    {
        EventType = eventType;
    }

    public string EventType { get; }
}
