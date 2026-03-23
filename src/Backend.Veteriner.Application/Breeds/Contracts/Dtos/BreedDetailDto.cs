namespace Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;

public sealed record BreedDetailDto(
    Guid Id,
    Guid SpeciesId,
    string SpeciesCode,
    string SpeciesName,
    string Name,
    bool IsActive);
