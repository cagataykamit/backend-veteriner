using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetInviteById;

/// <param name="TenantId">Route <c>tenantId</c>; JWT <c>tenant_id</c> ile eşleşmeli.</param>
/// <param name="InviteId">Route <c>inviteId</c>.</param>
public sealed record GetTenantInviteByIdQuery(Guid TenantId, Guid InviteId)
    : IRequest<Result<TenantInviteDetailDto>>;
