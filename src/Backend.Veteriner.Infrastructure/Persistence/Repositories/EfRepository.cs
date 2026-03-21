using Ardalis.Specification.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Okuma + yazma i�lemleri i�in generic repository.
/// Specification pattern desteklidir.
/// </summary>
public class EfRepository<T> : RepositoryBase<T>, IRepository<T> where T : class
{
    public EfRepository(AppDbContext dbContext) : base(dbContext) { }
}
