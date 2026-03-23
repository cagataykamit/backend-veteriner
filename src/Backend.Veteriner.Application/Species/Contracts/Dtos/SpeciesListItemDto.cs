namespace Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;

public sealed record SpeciesListItemDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    int DisplayOrder);
