namespace Backend.Veteriner.Application.PetColors.Contracts.Dtos;

public sealed record PetColorListItemDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    int DisplayOrder);
