using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Commands.Update;

public sealed record UpdatePetCommand(
    Guid Id,
    Guid ClientId,
    string Name,
    Guid SpeciesId,
    string? Breed = null,
    DateOnly? BirthDate = null,
    Guid? BreedId = null,
    PetGender? Gender = null,
    Guid? ColorId = null,
    decimal? Weight = null,
    string? Notes = null)
    : IRequest<Result>;