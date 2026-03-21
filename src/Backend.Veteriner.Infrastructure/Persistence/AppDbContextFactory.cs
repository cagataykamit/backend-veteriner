using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;


namespace Backend.Veteriner.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // 1) appsettings.json + appsettings.Development.json + user secrets (varsa)
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var basePath = Directory.GetCurrentDirectory(); // EF CLI, Infrastructure projesi k�k�n� baz al�r

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .AddEnvironmentVariables();

        var config = builder.Build();

        // 2) Connection string�i oku (ENV override destekli)
        var cs = config.GetConnectionString("SqlServer")
                 ?? Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")
                 ?? "Server=localhost;Database=VeterinerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(cs);

        return new AppDbContext(optionsBuilder.Options);
    }
}
