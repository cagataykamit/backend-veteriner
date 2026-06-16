using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        // KÖK NEDEN DÜZELTMESİ (config seviyesi — savunma katmanı):
        // AddBackendAppConfiguration JSON kaynaklarından sonra AddEnvironmentVariables() çağırır;
        // makinedeki ConnectionStrings__DefaultConnection ortam değişkeni appsettings.IntegrationTests.json
        // değerini ezip host'u VeterinerDb'ye yönlendiriyordu. Her iki connection anahtarını da
        // dedicated test string'ine sabitliyoruz. (Minimal hosting'de bu kaynak env var'dan düşük
        // öncelikli kalabildiği için tek başına yeterli değildir; asıl garanti aşağıdaki servis
        // seviyesi yeniden kayıt ile sağlanır.)
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = IntegrationTestDatabaseGuard.DedicatedConnectionString,
                ["ConnectionStrings:SqlServer"] = IntegrationTestDatabaseGuard.DedicatedConnectionString,
                ["ConnectionStrings:QueryConnection"] = IntegrationTestDatabaseGuard.DedicatedQueryConnectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            // ASIL GARANTİ: AppDbContext'i config önceliğinden bağımsız olarak dedicated test DB'ye
            // yeniden bağla (env var override edilemese dahi).
            IntegrationTestDbContextOverride.UseDedicatedDatabase(
                services, IntegrationTestDatabaseGuard.DedicatedConnectionString);

            IntegrationTestDbContextOverride.UseDedicatedQueryDatabase(
                services, IntegrationTestDatabaseGuard.DedicatedQueryConnectionString);

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            // GÜVENLİK KONTROLÜ: EnsureDeleted/Migrate/Seed'den ÖNCE efektif veritabanı adını doğrula.
            // VeterinerDb / Development / Production / boş ad görülürse DB bağlantısı açılmadan durur.
            var databaseName = IntegrationTestDatabaseGuard.EnsureSafeDatabase(db.Database.GetConnectionString());

            // Suite başında bir kere: EnsureDeleted + Migrate + Seed (sonraki sınıflar tekrar etmez).
            IntegrationTestDatabaseInitializer.EnsureResetMigratedAndSeeded(db, hasher, databaseName);
        });
    }
}
