using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext tabanlı Unit of Work implementasyonu.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public EfUnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}