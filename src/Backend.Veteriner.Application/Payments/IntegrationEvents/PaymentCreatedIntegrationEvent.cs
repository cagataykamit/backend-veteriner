namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// Yeni bir Payment başarıyla oluşturulduğunda üretilir.
/// </summary>
public sealed record PaymentCreatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    PaymentProjectionSnapshot Current);
