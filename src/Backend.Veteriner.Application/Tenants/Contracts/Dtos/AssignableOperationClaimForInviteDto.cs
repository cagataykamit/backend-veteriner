namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>Davet ekranı rol seçimi: gerçek <see cref="Backend.Veteriner.Domain.Auth.OperationClaim"/> Id.</summary>
public sealed record AssignableOperationClaimForInviteDto(
    Guid OperationClaimId,
    string OperationClaimName);
