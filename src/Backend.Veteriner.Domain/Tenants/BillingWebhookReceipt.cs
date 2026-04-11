namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Provider webhook idempotency: aynı <see cref="ProviderEventId"/> ikinci kez işlenmez.
/// </summary>
public sealed class BillingWebhookReceipt
{
    public Guid Id { get; private set; }
    public BillingProvider Provider { get; private set; }
    public string ProviderEventId { get; private set; } = default!;
    public string? EventType { get; private set; }
    public Guid? BillingCheckoutSessionId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }

    private BillingWebhookReceipt() { }

    public static BillingWebhookReceipt CreateReceived(
        BillingProvider provider,
        string providerEventId,
        string? eventType,
        Guid? billingCheckoutSessionId,
        string? correlationId,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(providerEventId))
            throw new ArgumentException("ProviderEventId gerekli.", nameof(providerEventId));

        return new BillingWebhookReceipt
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ProviderEventId = providerEventId.Trim(),
            EventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
            BillingCheckoutSessionId = billingCheckoutSessionId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            ReceivedAtUtc = utcNow,
            ProcessedAtUtc = null,
        };
    }

    public void MarkProcessed(DateTime utcNow)
    {
        ProcessedAtUtc = utcNow;
    }
}
