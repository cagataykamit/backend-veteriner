using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>Kiracı davet listesi öğesi. <c>IsExpired</c>: <see cref="TenantInviteStatus.Pending"/> iken süre dolmuşsa <c>true</c>.</summary>
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
    DateTime CreatedAtUtc);
