namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// Payment integration event'lerini transactional outbox buffer'a serialize ederek kuyruğa alır.
/// Buffer ile aynı HTTP isteği içindeki SaveChanges ile (aynı transaction sınırında) kalıcı olur.
/// </summary>
public interface IPaymentIntegrationEventOutbox
{
    Task EnqueueAsync<TEvent>(string eventType, TEvent integrationEvent, CancellationToken ct = default)
        where TEvent : notnull;
}
