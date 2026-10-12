namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// Bir Payment iptal edildiğinde ("hiç alınmamış" sayılır) üretilir. Projeksiyon ödemenin read-model satırını
/// ve günlük katkısını kaldırır; <see cref="Current"/> iptal anındaki anlık görüntüdür.
/// </summary>
public sealed record PaymentVoidedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    PaymentProjectionSnapshot Current);
