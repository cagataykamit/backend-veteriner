namespace Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;

public sealed record BreedListItemDto(
    Guid Id,
    Guid SpeciesId,
    string SpeciesName,
    string Name,
    bool IsActive);
