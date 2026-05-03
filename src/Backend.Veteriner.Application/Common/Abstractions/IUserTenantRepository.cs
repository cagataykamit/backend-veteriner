namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>Kullanıcı–kiracı üyelik sorguları (login/refresh doğrulaması).</summary>
public interface IUserTenantRepository
{
    Task<IReadOnlyList<Guid>> GetTenantIdsByUserIdAsync(Guid userId, CancellationToken ct);

    Task<bool> ExistsAsync(Guid userId, Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Verilen kullanıcı Id listesinden bu kiracıda UserTenant üyeliği olanların kümesini döner (tek sorgu).
    /// </summary>
    Task<IReadOnlySet<Guid>> GetExistingUserIdsForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct);

    /// <summary>Kullanıcının mevcut kiracı üyeliği varsa o kiracının Id'si (tek satır modeli).</summary>
    Task<Guid?> GetExistingTenantIdForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Kiracıda bu operation claim'e sahip kaç üye var (UserTenant ∩ UserOperationClaim).</summary>
    Task<int> CountMembersHavingOperationClaimAsync(Guid tenantId, Guid operationClaimId, CancellationToken ct);

    /// <summary>Kiracı üyelik satırını siler; yoksa false.</summary>
    Task<bool> TryRemoveMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct);
}
