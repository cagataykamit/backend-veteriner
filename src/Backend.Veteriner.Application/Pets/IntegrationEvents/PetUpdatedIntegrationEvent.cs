namespace Backend.Veteriner.Application.Pets.IntegrationEvents;

/// <summary>
/// Mevcut bir Pet başarıyla güncellendiğinde üretilir.
/// Read-model upsert için <see cref="Current"/> tek başına yeterlidir.
/// </summary>
public sealed record PetUpdatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    PetProjectionSnapshot Current);
