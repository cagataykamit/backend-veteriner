using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Commands.Update;

public sealed record UpdatePetCommand(
    Guid Id,
    Guid ClientId,
    string Name,
    Guid SpeciesId,
    string? Breed = null,
    DateOnly? BirthDate = null)
    : IRequest<Result>;