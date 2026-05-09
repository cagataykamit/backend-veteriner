using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Activate;

public sealed record ActivateProductCommand(Guid Id) : IRequest<Result>;
