using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

public sealed record PetDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClientId,
    string ClientName,
    string? ClientPhone,
    string? ClientEmail,
    string Name,
    Guid SpeciesId,
    string SpeciesName,
    Guid? ColorId,
    string? ColorName,
    string? Breed,
    DateOnly? BirthDate,
    Guid? BreedId,
    PetGender? Gender,
    decimal? Weight,
    string? Notes);
