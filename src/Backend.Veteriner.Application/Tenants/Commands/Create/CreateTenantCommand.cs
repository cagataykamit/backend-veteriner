using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Create;

public sealed record CreateTenantCommand(string Name) : IRequest<Result<Guid>>;
