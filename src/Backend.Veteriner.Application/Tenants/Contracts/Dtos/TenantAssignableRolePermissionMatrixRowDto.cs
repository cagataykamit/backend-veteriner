namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Whitelist’teki tek bir davet rolü ve DB’deki <c>OperationClaimPermission</c> kümesi (boş olabilir).
/// </summary>
public sealed record TenantAssignableRolePermissionMatrixRowDto(
    Guid OperationClaimId,
    string OperationClaimName,
    IReadOnlyList<TenantAssignableRolePermissionItemDto> Permissions);
