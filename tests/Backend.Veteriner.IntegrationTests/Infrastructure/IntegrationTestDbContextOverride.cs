using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// <see cref="AppDbContext"/> kaydını, uygulamanın configuration'dan türettiği connection string'i
/// (env var dahil) GÖZ ARDI ederek dedicated test connection string'ine yeniden bağlar.
///
/// Neden config override değil: <c>builder.ConfigureAppConfiguration</c> ile eklenen in-memory kaynak,
/// minimal hosting'de uygulamanın <c>AddBackendAppConfiguration</c> içindeki <c>AddEnvironmentVariables()</c>
/// kaynağından DAHA DÜŞÜK önceliklidir; bu yüzden makinedeki <c>ConnectionStrings__DefaultConnection</c>
/// ortam değişkeni config seviyesinde override edilemiyor. Servis seviyesinde yeniden kayıt, config
/// önceliğinden bağımsız ve deterministiktir. Production interceptor zinciri birebir korunur.
/// </summary>
internal static class IntegrationTestDbContextOverride
{
    public static void UseDedicatedDatabase(IServiceCollection services, string connectionString)
    {
        var toRemove = services
            .Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && d.ServiceType.GetGenericArguments() is [var arg] && arg == typeof(AppDbContext)))
            .ToList();

        foreach (var descriptor in toRemove)
            services.Remove(descriptor);

        // Production (AddInfrastructure) ile aynı interceptor zinciri.
        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseSqlServer(connectionString);
            opt.AddInterceptors(
                sp.GetRequiredService<OutboxSaveChangesInterceptor>(),
                sp.GetRequiredService<SlowQueryLoggingInterceptor>(),
                sp.GetRequiredService<DbConnectionSlowOpenInterceptor>());
        });
    }
}
