namespace Backend.Veteriner.Application.Projections.Payments;

public sealed record PaymentProjectionHealthEvaluation(
    PaymentProjectionHealthLevel Level,
    string Description,
    IReadOnlyDictionary<string, object?> Data);
