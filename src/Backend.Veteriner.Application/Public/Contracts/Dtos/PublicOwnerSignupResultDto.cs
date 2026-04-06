namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

public sealed record PublicOwnerSignupResultDto(
    Guid TenantId,
    Guid ClinicId,
    Guid UserId,
    string PlanCode,
    DateTime TrialStartsAtUtc,
    DateTime TrialEndsAtUtc,
    bool CanLogin,
    string NextStep);
