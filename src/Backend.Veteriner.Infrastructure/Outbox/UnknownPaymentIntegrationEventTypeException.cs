namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Bilinmeyen payment integration event type için fırlatılır.
/// </summary>
public sealed class UnknownPaymentIntegrationEventTypeException : Exception
{
    public UnknownPaymentIntegrationEventTypeException(string eventType)
        : base($"Unknown payment integration event type: '{eventType}'.")
    {
        EventType = eventType;
    }

    public string EventType { get; }
}
