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
    PetGender? Gender = null,
    Guid? ColorId = null,
    decimal? Weight = null,
    string? Notes = null,
    string? MicrochipNumber = null,
    string? PassportOrTagNumber = null,
    string? SpecialProtocolNumber = null,
    bool IsNeutered = false,
    IReadOnlyList<string>? AlertFlags = null,
    string? AlertNote = null)
    : IRequest<Result<Guid>>;
