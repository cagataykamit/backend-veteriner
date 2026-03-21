using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.OperationClaimPermissions;

/// <summary>
/// OperationClaim (rol) - Permission ili�kisini y�neten repository.
/// - Claim/Permission ba�lama-��zme i�lemleri
/// - Kullan�c�n�n efektif permission kodlar�n� �retme
/// - Cache invalidation i�in etkilenen userId setlerini sorgulama
///
/// Kurumsal prensip:
/// - Repository metotlar� SaveChanges �a��rmaz.
/// - Transaction/commit s�n�r� handler (application) seviyesinde y�netilir.
/// </summary>
public sealed class OperationClaimPermissionRepository : IOperationClaimPermissionRepository
{
    private readonly AppDbContext _db;

    public OperationClaimPermissionRepository(AppDbContext db)
        => _db = db;

    public Task<bool> ExistsAsync(Guid claimId, Guid permissionId, CancellationToken ct)
        => _db.OperationClaimPermissions
              .AnyAsync(x => x.OperationClaimId == claimId && x.PermissionId == permissionId, ct);

    public Task AddAsync(Guid claimId, Guid permissionId, CancellationToken ct)
    {
        // SaveChanges burada yok; commit handler/UoW seviyesinde yap�l�r.
        _db.OperationClaimPermissions.Add(new OperationClaimPermission(claimId, permissionId));
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(Guid claimId, Guid permissionId, CancellationToken ct)
    {
        var entity = await _db.OperationClaimPermissions
            .FirstOrDefaultAsync(x => x.OperationClaimId == claimId && x.PermissionId == permissionId, ct);

        if (entity is null) return;

        // SaveChanges burada yok; commit handler/UoW seviyesinde yap�l�r.
        _db.OperationClaimPermissions.Remove(entity);
    }

    /// <summary>
    /// Kullan�c�n�n efektif permission code listesini d�nd�r�r.
    /// Zincir:
    /// User -> UserOperationClaim -> OperationClaimPermission -> Permission(Code)
    /// </summary>
    public async Task<IReadOnlyList<string>> GetPermissionCodesByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var codes = await (
            from uoc in _db.UserOperationClaims
            join ocp in _db.OperationClaimPermissions on uoc.OperationClaimId equals ocp.OperationClaimId
            join p in _db.Permissions on ocp.PermissionId equals p.Id
            where uoc.UserId == userId
            select p.Code
        )
        .Distinct()
        .ToListAsync(ct);

        return codes;
    }

    /// <summary>
    /// Bir claim (rol) de�i�ti�inde etkilenecek kullan�c�lar� d�nd�r�r.
    /// Cache invalidation i�in kullan�l�r.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetUserIdsByOperationClaimIdAsync(Guid claimId, CancellationToken ct)
    {
        return await _db.UserOperationClaims
            .Where(x => x.OperationClaimId == claimId)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Bir permission de�i�ti�inde (update/delete) etkilenecek kullan�c�lar� d�nd�r�r.
    /// Zincir:
    /// Permission -> OperationClaimPermission -> UserOperationClaim -> UserId
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetUserIdsByPermissionIdAsync(Guid permissionId, CancellationToken ct)
    {
        return await (
            from ocp in _db.OperationClaimPermissions
            join uoc in _db.UserOperationClaims on ocp.OperationClaimId equals uoc.OperationClaimId
            where ocp.PermissionId == permissionId
            select uoc.UserId
        )
        .Distinct()
        .ToListAsync(ct);
    }

    /// <summary>
    /// Verilen permission'a ba�l� t�m OperationClaimPermission kay�tlar�n� kald�r�r.
    /// Not: ExecuteDelete kullan�lmaz; de�i�iklikler UoW commit'i ile kal�c�la��r.
    /// </summary>
    public async Task RemoveAllByPermissionIdAsync(Guid permissionId, CancellationToken ct)
    {
        var entities = await _db.OperationClaimPermissions
            .Where(x => x.PermissionId == permissionId)
            .ToListAsync(ct);

        if (entities.Count == 0) return;

        _db.OperationClaimPermissions.RemoveRange(entities);
    }
}