using Backend.Veteriner.Infrastructure.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>Design-time: <c>dotnet ef</c> API projesini derlemeden migration üretmek için.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=VetinityCommandDb_EfDesign;Trusted_Connection=True;TrustServerCertificate=True;");
        // Tasarım zamanında HTTP isteği ve kiracı yoktur; kiracı filtresi kapalı varsayılanla kalır (migration sorgu çalıştırmaz).
        return new AppDbContext(optionsBuilder.Options, new TenantContext(new HttpContextAccessor()));
    }
}
