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
            .OrderBy(x => x.TenantId)
            .Select(x => x.TenantId)
            .ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid userId, Guid tenantId, CancellationToken ct)
        => _db.UserTenants.AsNoTracking().AnyAsync(x => x.UserId == userId && x.TenantId == tenantId, ct);
}
