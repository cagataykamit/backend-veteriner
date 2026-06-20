namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetTextSearchLookupResult(IReadOnlyList<Guid> PetIds);
