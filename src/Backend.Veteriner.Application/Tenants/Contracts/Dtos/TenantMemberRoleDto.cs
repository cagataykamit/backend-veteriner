namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Kiracı üyesi rol özeti. Yalnızca <see cref="InviteAssignableOperationClaimsCatalog"/> whitelist'indeki
/// claim'ler bu listede görünür; teknik/internal claim'ler (ör. Admin.Diagnostics) tenant panelinde gizlenir.
/// </summary>
public sealed record TenantMemberRoleDto(
    Guid OperationClaimId,
    string OperationClaimName);
