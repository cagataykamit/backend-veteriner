using Backend.Veteriner.Domain.Authorization;

namespace Backend.Veteriner.Domain.Auth;

public sealed class OperationClaimPermission
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OperationClaimId { get; private set; }
    public Guid PermissionId { get; private set; }

    // Navigation
    public Permission? Permission { get; private set; }

    private OperationClaimPermission() { }

    public OperationClaimPermission(Guid operationClaimId, Guid permissionId)
    {
        OperationClaimId = operationClaimId;
        PermissionId = permissionId;
    }
}
