using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Create;

public sealed record CreateClientCommand(Guid TenantId, string FullName, string? Phone = null)
    : IRequest<Result<Guid>>;
