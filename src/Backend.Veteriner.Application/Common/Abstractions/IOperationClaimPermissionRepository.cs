namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IOperationClaimPermissionRepository
{
    Task<bool> ExistsAsync(Guid claimId, Guid permissionId, CancellationToken ct);
    Task AddAsync(Guid claimId, Guid permissionId, CancellationToken ct);
    Task RemoveAsync(Guid claimId, Guid permissionId, CancellationToken ct);

    // ? Kullan�c�n�n efektif permission kodlar�n� d�nd�r
    Task<IReadOnlyList<string>> GetPermissionCodesByUserIdAsync(Guid userId, CancellationToken ct);

    // Yeni: Bir role (operationClaim) ba�l� kullan�c�lar� d�nd�r
    Task<IReadOnlyList<Guid>> GetUserIdsByOperationClaimIdAsync(Guid claimId, CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetUserIdsByPermissionIdAsync(Guid permissionId, CancellationToken ct);

    Task RemoveAllByPermissionIdAsync(Guid permissionId, CancellationToken ct);

}
