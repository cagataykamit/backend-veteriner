namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// Mevcut bir Payment başarıyla güncellendiğinde üretilir.
/// </summary>
public sealed record PaymentUpdatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    PaymentProjectionSnapshot Current);
