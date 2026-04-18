using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMemberRole;

/// <summary>
/// Tenant paneli: mevcut bir üyeden rol (OperationClaim) kaldırır.
/// Idempotent: ilişki zaten yoksa <c>AlreadyRemoved = true</c> döner.
/// Self-protect: çağıran kullanıcı kendi üzerinden rol çıkaramaz (<c>Invites.SelfRoleRemoveForbidden</c>).
/// </summary>
public sealed record RemoveTenantMemberRoleCommand(Guid TenantId, Guid MemberId, Guid OperationClaimId)
    : IRequest<Result<RemoveTenantMemberRoleResultDto>>;
