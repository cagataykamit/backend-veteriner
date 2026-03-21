namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>Kullanıcı–kiracı üyelik sorguları (login/refresh doğrulaması).</summary>
public interface IUserTenantRepository
{
    Task<IReadOnlyList<Guid>> GetTenantIdsByUserIdAsync(Guid userId, CancellationToken ct);

    Task<bool> ExistsAsync(Guid userId, Guid tenantId, CancellationToken ct);
}
