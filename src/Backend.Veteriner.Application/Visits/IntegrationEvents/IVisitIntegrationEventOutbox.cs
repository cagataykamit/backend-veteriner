using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.IntegrationEvents;

/// <summary>
/// Visit integration event'lerini transactional outbox buffer'a serialize ederek kuyruğa alır.
/// Buffer ile aynı SaveChanges içinde (aynı transaction sınırında) kalıcı olur.
/// </summary>
public interface IVisitIntegrationEventOutbox
{
    Task EnqueueAsync<TEvent>(string eventType, TEvent integrationEvent, CancellationToken ct = default)
        where TEvent : notnull;
}

public static class VisitIntegrationEventOutboxExtensions
{
    public static Task EnqueueCreatedAsync(
        this IVisitIntegrationEventOutbox outbox, Visit visit, CancellationToken ct = default)
        => outbox.EnqueueAsync(
            VisitIntegrationEventTypes.Created,
            new VisitCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, ToSnapshot(visit)),
            ct);

    public static Task EnqueueUpdatedAsync(
        this IVisitIntegrationEventOutbox outbox, Visit visit, CancellationToken ct = default)
        => outbox.EnqueueAsync(
            VisitIntegrationEventTypes.Updated,
            new VisitUpdatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, ToSnapshot(visit)),
            ct);

    public static VisitProjectionSnapshot ToSnapshot(this Visit v)
        => new(
            v.Id,
            v.TenantId,
            v.ClinicId,
            v.PetId,
            v.AppointmentId,
            v.ResponsibleVeterinarianUserId,
            v.ArrivedAtUtc,
            (int)v.CareStatus,
            v.VoidedAtUtc,
            v.MutationSequence);
}
