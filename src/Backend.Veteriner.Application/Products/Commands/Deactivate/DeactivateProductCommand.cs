using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Deactivate;

public sealed record DeactivateProductCommand(Guid Id) : IRequest<Result>;
