using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAssignableOperationClaimsForInvite;

public sealed record GetAssignableOperationClaimsForInviteQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<AssignableOperationClaimForInviteDto>>>;
