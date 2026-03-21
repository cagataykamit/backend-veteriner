using Ardalis.Specification.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Sadece okuma s�zle�mesini uygular ama altyap�da
/// RepositoryBase<T> kullan�r (9.x i�in �nerilen yol).
/// </summary>
public class EfReadRepository<T> : RepositoryBase<T>, IReadRepository<T> where T : class
{
    public EfReadRepository(AppDbContext dbContext) : base(dbContext) { }
}
