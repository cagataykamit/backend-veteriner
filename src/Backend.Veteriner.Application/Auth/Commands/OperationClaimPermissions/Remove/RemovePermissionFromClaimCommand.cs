using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Remove;

public sealed record RemovePermissionFromClaimCommand(Guid OperationClaimId, Guid PermissionId)
    : IRequest, IAuditableRequest
{
    public string AuditAction => "OperationClaimPermission.Remove";
    public string AuditTarget => $"OperationClaimId={OperationClaimId}, PermissionId={PermissionId}";
}