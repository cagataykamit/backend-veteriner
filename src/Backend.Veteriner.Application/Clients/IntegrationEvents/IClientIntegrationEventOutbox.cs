namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// Client integration event'lerini transactional outbox buffer'a serialize ederek kuyruğa alır.
/// Buffer ile aynı HTTP isteği içindeki SaveChanges ile (aynı transaction sınırında) kalıcı olur.
/// </summary>
public interface IClientIntegrationEventOutbox
{
    Task EnqueueAsync<TEvent>(string eventType, TEvent integrationEvent, CancellationToken ct = default)
        where TEvent : notnull;
}
