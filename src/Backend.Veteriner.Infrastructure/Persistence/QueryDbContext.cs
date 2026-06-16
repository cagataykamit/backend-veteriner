using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// CQRS query tarafı için ayrı SQL Server veritabanı context'i.
/// Projection read-model entity'leri ileride bu context üzerinden yönetilecek.
/// </summary>
public sealed class QueryDbContext : DbContext
{
    public QueryDbContext(DbContextOptions<QueryDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // CQRS-3: Appointment projection entity configuration'ları buraya eklenecek.
    }
}
