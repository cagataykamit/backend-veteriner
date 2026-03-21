using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Permissions;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly AppDbContext _db;
    public PermissionRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct)
        => _db.Set<Permission>().AnyAsync(x => x.Code == code, ct);

    public Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Set<Permission>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Permission?> GetByCodeAsync(string code, CancellationToken ct)
        => _db.Set<Permission>().FirstOrDefaultAsync(x => x.Code == code, ct);

    public async Task AddAsync(Permission permission, CancellationToken ct)
    {
        _db.Add(permission);
        await _db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(Permission entity, CancellationToken ct)
    {
        _db.Permissions.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Permission entity, CancellationToken ct)
    {
        _db.Permissions.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PermissionDto>> GetListAsync(string? search, CancellationToken ct)
    {
        var q = _db.Set<Permission>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(x => x.Code.Contains(search) || (x.Description ?? "").Contains(search));

        return await q
            .OrderBy(x => x.Code)
            .Select(x => new PermissionDto(x.Id, x.Code, x.Description))
            .ToListAsync(ct);
    }
}
