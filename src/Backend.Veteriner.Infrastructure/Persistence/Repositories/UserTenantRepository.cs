using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

public sealed class UserTenantRepository : IUserTenantRepository
{
    private readonly AppDbContext _db;

    public UserTenantRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> GetTenantIdsByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return await _db.UserTenants
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.TenantId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid userId, Guid tenantId, CancellationToken ct)
        => _db.UserTenants.AsNoTracking().AnyAsync(x => x.UserId == userId && x.TenantId == tenantId, ct);

    public async Task<Guid?> GetExistingTenantIdForUserAsync(Guid userId, CancellationToken ct)
    {
        return await _db.UserTenants
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.TenantId)
            .FirstOrDefaultAsync(ct);
    }

    public Task<int> CountMembersHavingOperationClaimAsync(Guid tenantId, Guid operationClaimId, CancellationToken ct)
    {
        return (
            from ut in _db.UserTenants.AsNoTracking()
            join uoc in _db.UserOperationClaims.AsNoTracking() on ut.UserId equals uoc.UserId
            where ut.TenantId == tenantId && uoc.OperationClaimId == operationClaimId
            select ut.UserId
        ).Distinct().CountAsync(ct);
    }

    public async Task<bool> TryRemoveMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var row = await _db.UserTenants
            .FirstOrDefaultAsync(x => x.UserId == userId && x.TenantId == tenantId, ct);
        if (row is null)
            return false;

        _db.UserTenants.Remove(row);
        return true;
    }
}
