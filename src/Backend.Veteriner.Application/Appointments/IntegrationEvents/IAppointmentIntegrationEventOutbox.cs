namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

/// <summary>
/// Appointment integration event'lerini transactional outbox buffer'a serialize ederek kuyruğa alır.
/// <see cref="IOutboxBuffer"/> ile aynı HTTP isteği içindeki SaveChanges ile kalıcı olur.
/// </summary>
public interface IAppointmentIntegrationEventOutbox
{
    Task EnqueueAsync<TEvent>(string eventType, TEvent integrationEvent, CancellationToken ct = default)
        where TEvent : notnull;
}
