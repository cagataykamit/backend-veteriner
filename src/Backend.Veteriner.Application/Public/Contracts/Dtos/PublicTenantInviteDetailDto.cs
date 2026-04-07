namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

public sealed record PublicTenantInviteDetailDto(
    string InviteToken,
    Guid TenantId,
    string TenantName,
    Guid ClinicId,
    string ClinicName,
    string Email,
    DateTime ExpiresAtUtc,
    bool IsExpired,
    bool IsPending,
    bool CanJoin,
    bool RequiresLogin,
    bool RequiresSignup);
