using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.CancelInvite;

public sealed record CancelTenantInviteCommand(Guid TenantId, Guid InviteId)
    : IRequest<Result<CancelTenantInviteResultDto>>;
