namespace Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;

public sealed record SpeciesDetailDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    int DisplayOrder);
