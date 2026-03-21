namespace Backend.Veteriner.Application.Auth.Contracts.Dtos;

/// <summary>
/// Kullanıcı–rol (OperationClaim) ilişki kaydı.
/// </summary>
public sealed record UserOperationClaimDto(
    Guid Id,
    Guid UserId,
    Guid OperationClaimId
);
