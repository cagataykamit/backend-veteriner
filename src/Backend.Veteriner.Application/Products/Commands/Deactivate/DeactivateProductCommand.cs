using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Commands.Deactivate;

public sealed record DeactivateProductCommand(Guid Id) : IRequest<Result>;
