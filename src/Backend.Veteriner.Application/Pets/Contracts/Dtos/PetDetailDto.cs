using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

public sealed record PetDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClientId,
    string Name,
    Guid SpeciesId,
    string SpeciesName,
    string? Breed,
    DateOnly? BirthDate,
    Guid? BreedId,
    PetGender? Gender);
