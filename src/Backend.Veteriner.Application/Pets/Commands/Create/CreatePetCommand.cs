using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Commands.Create;

public sealed record CreatePetCommand(
    Guid ClientId,
    string Name,
    Guid SpeciesId,
    string? Breed = null,
    DateOnly? BirthDate = null,
    Guid? BreedId = null,
    PetGender? Gender = null)
    : IRequest<Result<Guid>>;
