namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetDisplayLookupResult(IReadOnlyList<PetDisplayLookupItem> Items);
