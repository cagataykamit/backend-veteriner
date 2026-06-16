using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Paylaşılan VetinityDb_IntegrationTests veritabanında OutboxProcessor çakışmasını önlemek için
/// ayrı LocalDB veritabanı ve hızlı döngü/no-op SMTP kullanır.
/// </summary>
public sealed class OutboxProcessorWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private const string ProcessorTestDatabaseName = "VetinityDb_OutboxProcessorTests";

    private const string ProcessorTestConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityDb_OutboxProcessorTests;Trusted_Connection=True;MultipleActiveResultSets=true";

    private const string ProcessorTestQueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_OutboxProcessorTests;Trusted_Connection=True;MultipleActiveResultSets=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        // Bkz. CustomWebApplicationFactory: ConnectionStrings__DefaultConnection ortam değişkeninin
        // ezme riskine karşı her iki anahtarı da dedicated test connection string'e sabitle.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ProcessorTestConnection,
                ["ConnectionStrings:SqlServer"] = ProcessorTestConnection,
                ["ConnectionStrings:QueryConnection"] = ProcessorTestQueryConnection
            }.Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });

        builder.ConfigureServices(services =>
        {
            // Servis seviyesi yeniden kayıt (config önceliğinden bağımsız, env var override edilemese dahi).
            IntegrationTestDbContextOverride.UseDedicatedDatabase(services, ProcessorTestConnection);
            IntegrationTestDbContextOverride.UseDedicatedQueryDatabase(services, ProcessorTestQueryConnection);

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            // EnsureCreated'den önce efektif veritabanı adını doğrula (VeterinerDb/Development/Production engeli).
            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                db.Database.GetConnectionString(),
                allowedPrefix: ProcessorTestDatabaseName);

            // Deterministik kırılganlık düzeltmesi: EnsureCreated() LocalDB auto-close/stale durumda
            // "already exists" üretebiliyor. Hosted OutboxProcessor ve gerçek DbContext bağlantıları
            // açılmadan ÖNCE (host build aşaması) mevcut güvenli reset ile stale DB'yi drop et,
            // ardından şemayı temiz biçimde yeniden oluştur. Yeni SQL drop implementasyonu yazılmadı.
            IntegrationTestDatabaseReset.EnsureDroppedAsync(ProcessorTestConnection).GetAwaiter().GetResult();

            db.Database.EnsureCreated();
            TestDataSeeder.Seed(db, hasher);

            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                queryDb.Database.GetConnectionString(),
                allowedPrefix: "VetinityQueryDb_OutboxProcessorTests");
            IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb).GetAwaiter().GetResult();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            services.PostConfigure<OutboxOptions>(o =>
            {
                o.LoopIntervalSeconds = 2;
                o.BatchSize = 50;
            });
        });
    }

    // Dispose sırasında: önce host (ve hosted OutboxProcessor + tüm DbContext bağlantıları) kapatılır,
    // ardından test DB'si güvenli force-drop ile kaldırılır; LocalDB'de stale/bozuk DB veya .mdf/.ldf kalmaz.
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(ProcessorTestConnection);
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(ProcessorTestQueryConnection);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            IntegrationTestDatabaseReset.EnsureDroppedAsync(ProcessorTestConnection).GetAwaiter().GetResult();
            IntegrationTestDatabaseReset.EnsureDroppedAsync(ProcessorTestQueryConnection).GetAwaiter().GetResult();
        }
    }
}
