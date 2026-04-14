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

    /// <summary>
    /// Soğuk yolda model derleme maliyetini düşürmek için önceden derlenmiş okuma (tek round-trip, üçlü join).
    /// </summary>
    private static readonly Func<AppDbContext, Guid, IAsyncEnumerable<string>> PermissionCodesByUserIdCompiled =
        EF.CompileAsyncQuery((AppDbContext db, Guid userId) =>
            from uoc in db.UserOperationClaims.AsNoTracking()
            where uoc.UserId == userId
            join ocp in db.OperationClaimPermissions.AsNoTracking() on uoc.OperationClaimId equals ocp.OperationClaimId
            join p in db.Permissions.AsNoTracking() on ocp.PermissionId equals p.Id
            select p.Code);

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
        // Önceden derlenmiş sorgu: ilk çağrıda model derleme maliyetini düşürür; tek SELECT (üçlü join).
        // Sonuçları akış sırasında tekilleştirerek gereksiz materyalizasyonu azaltır.
        var uniqueCodes = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var code in PermissionCodesByUserIdCompiled(_db, userId).WithCancellation(ct))
            uniqueCodes.Add(code);

        if (uniqueCodes.Count == 0)
            return Array.Empty<string>();

        if (uniqueCodes.Count == 1)
            return [uniqueCodes.First()];

        var ordered = uniqueCodes.ToList();
        ordered.Sort(StringComparer.Ordinal);
        return ordered;
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
