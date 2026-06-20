namespace Backend.Veteriner.Application.Projections.Pets;

public sealed record PetProjectionHealthEvaluation(
    PetProjectionHealthLevel Level,
    string Description,
    IReadOnlyDictionary<string, object?> Data);
