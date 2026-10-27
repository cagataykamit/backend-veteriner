using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Okuma + yazma işlemleri için generic repository.
/// Specification pattern desteklidir.
///
/// Yazım metotları (Add/Update/Delete ve Range sürümleri) yalnız değişiklik izleyicisine işler, <b>kaydetmez</b>
/// (ADR-039). Ardalis varsayılanı her çağrıda <c>SaveChanges</c> çalıştırır; bu, bir komutun yazımlarını ayrı
/// commit'lere böler. Commit noktası handler'daki <c>SaveChangesAsync</c> çağrısıdır (<see cref="IUnitOfWork"/>
/// ya da repository üzerinden; ikisi aynı DbContext'i kaydeder). Dönen <c>int</c> etkilenen satır sayısı değildir (0).
/// </summary>
public class EfRepository<T> : RepositoryBase<T>, IRepository<T> where T : class
{
    private readonly AppDbContext _db;
    private readonly DbSet<T> _set;

    public EfRepository(AppDbContext dbContext) : base(dbContext, TenantSafeSpecificationEvaluator.Instance)
    {
        _db = dbContext;
        _set = dbContext.Set<T>();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.CommitAsync(cancellationToken);

    public override Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        _set.Add(entity);
        return Task.FromResult(entity);
    }

    public override Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        _set.AddRange(entities);
        return Task.FromResult(entities);
    }

    public override Task<int> UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        _set.Update(entity);
        return Task.FromResult(0);
    }

    public override Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        _set.UpdateRange(entities);
        return Task.FromResult(0);
    }

    public override Task<int> DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        _set.Remove(entity);
        return Task.FromResult(0);
    }

    public override Task<int> DeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        _set.RemoveRange(entities);
        return Task.FromResult(0);
    }

    public override async Task<int> DeleteRangeAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        _set.RemoveRange(await ApplySpecification(specification).ToListAsync(cancellationToken));
        return 0;
    }
}
