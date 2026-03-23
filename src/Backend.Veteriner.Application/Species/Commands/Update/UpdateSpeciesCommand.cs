using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Commands.Update;

public sealed record UpdateSpeciesCommand(
    Guid Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive) : IRequest<Result>;
