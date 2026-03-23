using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Commands.Create;

public sealed record CreateSpeciesCommand(string Code, string Name, int DisplayOrder = 0)
    : IRequest<Result<Guid>>;
