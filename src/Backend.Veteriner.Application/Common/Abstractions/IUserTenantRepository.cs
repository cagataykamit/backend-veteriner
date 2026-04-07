namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>Kullanıcı–kiracı üyelik sorguları (login/refresh doğrulaması).</summary>
public interface IUserTenantRepository
{
    Task<IReadOnlyList<Guid>> GetTenantIdsByUserIdAsync(Guid userId, CancellationToken ct);

    Task<bool> ExistsAsync(Guid userId, Guid tenantId, CancellationToken ct);

    /// <summary>Kullanıcının mevcut kiracı üyeliği varsa o kiracının Id'si (tek satır modeli).</summary>
    Task<Guid?> GetExistingTenantIdForUserAsync(Guid userId, CancellationToken ct);
}
