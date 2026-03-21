using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Users.Commands.Claims.Remove;

/// <summary>
/// Admin: kullanıcıdan rol (operation claim) çıkarır.
/// </summary>
public sealed record AdminRemoveUserClaimCommand(
    Guid UserId,
    Guid OperationClaimId
) : IRequest, IAuditableRequest
{
    public string AuditAction => "UserClaim.Remove";
    public string AuditTarget => $"UserId={UserId}, OperationClaimId={OperationClaimId}";
}
