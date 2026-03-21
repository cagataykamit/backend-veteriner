namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

public sealed record PetDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClientId,
    string Name,
    string Species,
    string? Breed,
    DateOnly? BirthDate);
