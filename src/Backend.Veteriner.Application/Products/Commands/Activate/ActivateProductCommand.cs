using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Commands.Activate;

public sealed record ActivateProductCommand(Guid Id) : IRequest<Result>;
