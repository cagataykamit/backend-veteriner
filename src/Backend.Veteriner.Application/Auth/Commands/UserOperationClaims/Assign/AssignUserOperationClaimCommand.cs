using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Assign;

/// <summary>
/// Kullanıcıya OperationClaim (rol) atar.
/// </summary>
public sealed record AssignUserOperationClaimCommand(Guid UserId, Guid OperationClaimId)
    : IRequest<Result<Guid>>, IAuditableRequest
{
    public string AuditAction => "UserOperationClaim.Assign";
    public string AuditTarget => $"UserId={UserId}, OperationClaimId={OperationClaimId}";
}