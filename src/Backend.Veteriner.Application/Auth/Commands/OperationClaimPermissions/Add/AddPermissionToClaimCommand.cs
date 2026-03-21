using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Add;

public sealed record AddPermissionToClaimCommand(Guid OperationClaimId, Guid PermissionId)
    : IRequest, IAuditableRequest
{
    public string AuditAction => "OperationClaimPermission.Add";
    public string AuditTarget => $"OperationClaimId={OperationClaimId}, PermissionId={PermissionId}";
}