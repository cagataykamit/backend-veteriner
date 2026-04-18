using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.AssignMemberRole;

/// <summary>
/// Tenant paneli: mevcut bir üyeye whitelist içinden rol (OperationClaim) atar.
/// <c>TenantId</c> JWT ile eşleşmelidir; üye kiracıda değilse 404 <c>Members.NotFound</c>.
/// Idempotent: ilişki zaten varsa <c>AlreadyAssigned = true</c> döner.
/// </summary>
public sealed record AssignTenantMemberRoleCommand(Guid TenantId, Guid MemberId, Guid OperationClaimId)
    : IRequest<Result<AssignTenantMemberRoleResultDto>>;
