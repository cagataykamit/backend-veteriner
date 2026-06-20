using Backend.Veteriner.Application.Pets.Contracts.Dtos;

namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetListReadResult(
    IReadOnlyList<PetListItemDto> Items,
    int TotalCount);
