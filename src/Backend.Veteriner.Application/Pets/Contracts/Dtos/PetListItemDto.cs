namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

public sealed record PetListItemDto(
    Guid Id,
    Guid TenantId,
    Guid ClientId,
    string Name,
    string Species,
    string? Breed);
