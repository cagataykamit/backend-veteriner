namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// Mevcut bir Client başarıyla güncellendiğinde üretilir.
/// Read-model upsert için <see cref="Current"/> tek başına yeterlidir.
/// </summary>
public sealed record ClientUpdatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    ClientProjectionSnapshot Current);
