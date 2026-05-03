using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli için tek davet detayı. <see cref="TenantInviteListItemDto"/>'dan farklı olarak
/// <c>AcceptedAtUtc</c> ve <c>AcceptedByUserId</c> da içerir. Ham token asla bu DTO'da dönmez.
/// <c>IsExpired</c>: <see cref="TenantInviteStatus.Pending"/> iken süre dolmuşsa <c>true</c>.
/// <c>canCancelInvite</c> / <c>canResendInvite</c>: panel CTA’ları; yalnızca <see cref="TenantInviteStatus.Pending"/> iken <c>true</c>
/// (süresi dolmuş bekleyen davet iptal / resend komutlarıyla uyumlu).
/// <c>IsCurrentMember</c>: kabul eden kullanıcı bu kiracıda hâlâ üye mi (davet geçmişi <see cref="TenantInviteStatus.Accepted"/> kalır).
/// </summary>
public sealed record TenantInviteDetailDto(
    Guid Id,
    Guid TenantId,
    string Email,
    Guid ClinicId,
    string? ClinicName,
    Guid OperationClaimId,
    string? OperationClaimName,
    TenantInviteStatus Status,
    bool IsExpired,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc,
    DateTime? AcceptedAtUtc,
    Guid? AcceptedByUserId,
    bool CanCancelInvite,
    bool CanResendInvite,
    bool IsCurrentMember);
