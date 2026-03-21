using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;

/// <summary>
/// Kullanıcıdan OperationClaim (rol) kaldırır.
/// </summary>
public sealed record RemoveUserOperationClaimCommand(Guid UserId, Guid OperationClaimId)
    : IRequest, IAuditableRequest
{
    public string AuditAction => "UserOperationClaim.Remove";
    public string AuditTarget => $"UserId={UserId}, OperationClaimId={OperationClaimId}";
}