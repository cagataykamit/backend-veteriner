namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// Yeni bir Client başarıyla oluşturulduğunda üretilir.
/// <see cref="Current"/> read-model'i doldurmak için yeterli tüm alanları taşır.
/// </summary>
public sealed record ClientCreatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    ClientProjectionSnapshot Current);
