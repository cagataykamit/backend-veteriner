namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetIdsByClientIdsLookupResult(IReadOnlyList<Guid> PetIds);
