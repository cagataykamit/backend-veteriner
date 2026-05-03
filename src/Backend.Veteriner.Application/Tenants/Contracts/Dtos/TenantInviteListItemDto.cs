using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Kiracı davet listesi öğesi. <c>IsExpired</c>: <see cref="TenantInviteStatus.Pending"/> iken süre dolmuşsa <c>true</c>.
/// <c>IsCurrentMember</c>: yalnızca kabul edilmiş davet için daveti kabul eden kullanıcının hâlâ kiracı üyesi olup olmadığı.
/// </summary>
public sealed record TenantInviteListItemDto(
    Guid Id,
    string Email,
    Guid ClinicId,
    string? ClinicName,
    Guid OperationClaimId,
    string? OperationClaimName,
    TenantInviteStatus Status,
    bool IsExpired,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc,
    bool IsCurrentMember);
