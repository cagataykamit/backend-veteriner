namespace Backend.Veteriner.Application.Visits.IntegrationEvents;

public sealed record VisitCreatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    VisitProjectionSnapshot Current);

/// <summary>Bakım durumu geçişi, düzeltme veya yanlış geliş işaretinden sonra üretilir.</summary>
public sealed record VisitUpdatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    VisitProjectionSnapshot Current);
