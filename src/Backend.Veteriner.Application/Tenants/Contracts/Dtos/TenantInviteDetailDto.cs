using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli için tek davet detayı. <see cref="TenantInviteListItemDto"/>'dan farklı olarak
/// <c>AcceptedAtUtc</c> ve <c>AcceptedByUserId</c> da içerir. Ham token asla bu DTO'da dönmez.
/// <c>IsExpired</c>: <see cref="TenantInviteStatus.Pending"/> iken süre dolmuşsa <c>true</c>.
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
    Guid? AcceptedByUserId);
