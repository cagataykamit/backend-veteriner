using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Pets.Commands.Update;

/// <param name="HbsAnimalId">HBS hayvan kimlik no (ADR-036): <c>null</c> = dokunma, <c>""</c> = temizle.</param>
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
    string? Notes = null,
    string? MicrochipNumber = null,
    string? PassportOrTagNumber = null,
    string? SpecialProtocolNumber = null,
    bool IsNeutered = false,
    IReadOnlyList<string>? AlertFlags = null,
    string? AlertNote = null,
    string? HbsAnimalId = null)
    : IRequest<Result>;