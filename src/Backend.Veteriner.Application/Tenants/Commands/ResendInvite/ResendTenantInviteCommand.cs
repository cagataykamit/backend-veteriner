using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.ResendInvite;

/// <param name="TenantId">Route <c>tenantId</c>; JWT <c>tenant_id</c> ile eşleşmeli.</param>
/// <param name="InviteId">Route <c>inviteId</c>. Yalnız <see cref="Backend.Veteriner.Domain.Tenants.TenantInviteStatus.Pending"/> davet için çalışır.</param>
/// <param name="ExpiresAtUtc">Opsiyonel; yoksa <c>utcNow + InviteDefaults.DefaultExpiryDays</c> kullanılır.</param>
public sealed record ResendTenantInviteCommand(Guid TenantId, Guid InviteId, DateTime? ExpiresAtUtc)
    : IRequest<Result<ResendTenantInviteResultDto>>;
