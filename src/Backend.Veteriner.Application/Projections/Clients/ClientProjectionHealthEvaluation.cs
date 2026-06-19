namespace Backend.Veteriner.Application.Projections.Clients;

public sealed record ClientProjectionHealthEvaluation(
    ClientProjectionHealthLevel Level,
    string Description,
    IReadOnlyDictionary<string, object?> Data);
