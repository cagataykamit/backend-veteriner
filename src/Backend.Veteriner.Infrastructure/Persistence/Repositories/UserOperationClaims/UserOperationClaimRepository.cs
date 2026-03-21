using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.UserOperationClaims;

/// <summary>
/// UserOperationClaim repository (EF Core).
///
/// Kurumsal prensip:
/// - Repository metotları SaveChanges çağırmaz.
/// - Transaction / commit sınırı handler (application) seviyesinde,
///   IUnitOfWork.SaveChangesAsync(...) ile yönetilir.
/// </summary>
public sealed class UserOperationClaimRepository : IUserOperationClaimRepository
{
    private readonly AppDbContext _db;

    public UserOperationClaimRepository(AppDbContext db)
        => _db = db;

    public Task<bool> ExistsAsync(Guid userId, Guid operationClaimId, CancellationToken ct)
        => _db.UserOperationClaims
            .AnyAsync(x => x.UserId == userId && x.OperationClaimId == operationClaimId, ct);

    public Task AddAsync(UserOperationClaim entity, CancellationToken ct)
    {
        // SaveChanges burada yok; commit handler/UoW seviyesinde yapılır.
        _db.UserOperationClaims.Add(entity);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(Guid userId, Guid operationClaimId, CancellationToken ct)
    {
        var entity = await _db.UserOperationClaims
            .FirstOrDefaultAsync(x => x.UserId == userId && x.OperationClaimId == operationClaimId, ct);

        if (entity is null) return;

        // SaveChanges burada yok; commit handler/UoW seviyesinde yapılır.
        _db.UserOperationClaims.Remove(entity);
    }

    public Task<UserOperationClaim?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.UserOperationClaims
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<UserOperationClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return await _db.UserOperationClaims
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UserOperationClaimDetailDto>> GetDetailsByUserIdAsync(Guid userId, CancellationToken ct)
    {
        // UserEmail + ClaimName join
        return await (
            from uoc in _db.UserOperationClaims
            join u in _db.Users on uoc.UserId equals u.Id
            join oc in _db.OperationClaims on uoc.OperationClaimId equals oc.Id
            where uoc.UserId == userId
            orderby oc.Name
            select new UserOperationClaimDetailDto(
                uoc.Id,
                uoc.UserId,
                u.Email,
                uoc.OperationClaimId,
                oc.Name
            )
        ).ToListAsync(ct);
    }
}