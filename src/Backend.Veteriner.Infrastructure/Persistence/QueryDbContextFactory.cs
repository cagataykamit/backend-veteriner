using Backend.Veteriner.Infrastructure.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Design-time: <c>dotnet ef</c> ile Query DB migration üretmek için (yalnızca design-time).
/// Runtime connection string çözümlemesi yapmaz; <see cref="AppDbContextFactory"/> ile aynı DB adını kullanmaz.
/// </summary>
public sealed class QueryDbContextFactory : IDesignTimeDbContextFactory<QueryDbContext>
{
    public QueryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QueryDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_EfDesign;Trusted_Connection=True;TrustServerCertificate=True;");
        // Tasarım zamanında kiracı yoktur; kiracı filtresi kapalı varsayılanla kalır (migration sorgu çalıştırmaz).
        return new QueryDbContext(optionsBuilder.Options, new TenantContext(new HttpContextAccessor()));
    }
}
