using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Commands.Create;

public sealed record CreateBreedCommand(Guid SpeciesId, string Name) : IRequest<Result<Guid>>;
