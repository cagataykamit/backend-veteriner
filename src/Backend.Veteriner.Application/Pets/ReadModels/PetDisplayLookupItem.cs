namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetDisplayLookupItem(
    Guid PetId,
    Guid ClientId,
    string Name,
    Guid SpeciesId,
    string SpeciesName);
