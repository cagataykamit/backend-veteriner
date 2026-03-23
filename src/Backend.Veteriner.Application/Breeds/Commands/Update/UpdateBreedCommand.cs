using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Commands.Update;

public sealed record UpdateBreedCommand(Guid Id, string Name, bool IsActive) : IRequest<Result>;
