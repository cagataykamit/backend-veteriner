namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

public sealed record TenantInviteAcceptResultDto(
    Guid TenantId,
    Guid ClinicId,
    Guid UserId,
    string NextStep);
