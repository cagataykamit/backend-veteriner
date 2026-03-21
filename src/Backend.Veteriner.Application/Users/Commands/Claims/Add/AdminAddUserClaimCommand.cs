using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Users.Commands.Claims.Add;

/// <summary>
/// Admin: kullanıcıya rol (operation claim) atar.
/// </summary>
public sealed record AdminAddUserClaimCommand(
    Guid UserId,
    Guid OperationClaimId
) : IRequest, IAuditableRequest
{
    public string AuditAction => "UserClaim.Add";
    public string AuditTarget => $"UserId={UserId}, OperationClaimId={OperationClaimId}";
}
