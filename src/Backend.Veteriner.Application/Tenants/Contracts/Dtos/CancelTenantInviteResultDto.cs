using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Davet iptal sonucu. <c>AlreadyCancelled</c> = <c>true</c> ise istek idempotent yorumlanmıştır
/// (davet zaten <see cref="TenantInviteStatus.Revoked"/> durumundaydı; hata döndürülmedi).
/// </summary>
public sealed record CancelTenantInviteResultDto(
    Guid InviteId,
    TenantInviteStatus Status,
    bool AlreadyCancelled);
