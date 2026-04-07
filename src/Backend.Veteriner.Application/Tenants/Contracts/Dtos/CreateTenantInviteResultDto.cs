namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

public sealed record CreateTenantInviteResultDto(
    Guid InviteId,
    string Token,
    string Email,
    Guid TenantId,
    Guid ClinicId,
    DateTime ExpiresAtUtc);
