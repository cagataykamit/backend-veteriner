using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Commands.Create;

public sealed record CreatePetCommand(
    Guid TenantId,
    Guid ClientId,
    string Name,
    string Species,
    string? Breed = null,
    DateOnly? BirthDate = null)
    : IRequest<Result<Guid>>;
