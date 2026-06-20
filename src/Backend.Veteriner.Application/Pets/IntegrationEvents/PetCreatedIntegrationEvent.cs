namespace Backend.Veteriner.Application.Pets.IntegrationEvents;

/// <summary>
/// Yeni bir Pet başarıyla oluşturulduğunda üretilir.
/// <see cref="Current"/> read-model'i doldurmak için yeterli tüm alanları taşır.
/// </summary>
public sealed record PetCreatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    PetProjectionSnapshot Current);
