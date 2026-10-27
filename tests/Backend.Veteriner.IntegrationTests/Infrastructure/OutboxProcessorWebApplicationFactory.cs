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
/// Paylaşılan VetinityCommandDb_IntegrationTests veritabanında OutboxProcessor çakışmasını önlemek için
/// ayrı LocalDB veritabanı ve kayıt tutan sahte SMTP kullanır.
/// </summary>
public sealed class OutboxProcessorWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private static readonly string OutboxProcessorCommandDatabaseName = IntegrationTestDatabaseNames.Command("OutboxProcessorTests");
    private static readonly string OutboxProcessorQueryDatabaseName = IntegrationTestDatabaseNames.Query("OutboxProcessorTests");

    private static readonly string OutboxProcessorCommandConnection =
        IntegrationTestDatabaseNames.ConnectionString(OutboxProcessorCommandDatabaseName);

    private static readonly string OutboxProcessorQueryConnection =
        IntegrationTestDatabaseNames.ConnectionString(OutboxProcessorQueryDatabaseName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        // Bkz. CustomWebApplicationFactory: ConnectionStrings__DefaultConnection ortam değişkeninin
        // ezme riskine karşı her iki anahtarı da dedicated test connection string'e sabitle.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = OutboxProcessorCommandConnection,
                ["ConnectionStrings:SqlServer"] = OutboxProcessorCommandConnection,
                ["ConnectionStrings:QueryConnection"] = OutboxProcessorQueryConnection
            }.Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });

        builder.ConfigureServices(services =>
        {
            // Servis seviyesi yeniden kayıt (config önceliğinden bağımsız, env var override edilemese dahi).
            IntegrationTestDbContextOverride.UseDedicatedDatabase(services, OutboxProcessorCommandConnection);
            IntegrationTestDbContextOverride.UseDedicatedQueryDatabase(services, OutboxProcessorQueryConnection);

            using var scope = services.BuildServiceProvider().CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            // EnsureCreated'den önce efektif veritabanı adını doğrula (VetinityCommandDb/Development/Production engeli).
            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                db.Database.GetConnectionString(),
                allowedPrefix: OutboxProcessorCommandDatabaseName);

            // Deterministik kırılganlık düzeltmesi: EnsureCreated() LocalDB auto-close/stale durumda
            // "already exists" üretebiliyor. Hosted OutboxProcessor ve gerçek DbContext bağlantıları
            // açılmadan ÖNCE (host build aşaması) mevcut güvenli reset ile stale DB'yi drop et,
            // ardından şemayı temiz biçimde yeniden oluştur. Yeni SQL drop implementasyonu yazılmadı.
            IntegrationTestDatabaseReset.EnsureDroppedAsync(OutboxProcessorCommandConnection).GetAwaiter().GetResult();

            db.Database.EnsureCreated();
            TestDataSeeder.Seed(db, hasher);

            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                queryDb.Database.GetConnectionString(),
                allowedPrefix: OutboxProcessorQueryDatabaseName);
            IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb).GetAwaiter().GetResult();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddSingleton<RecordingEmailSenderImmediate>();
            services.AddSingleton<IEmailSenderImmediate>(sp => sp.GetRequiredService<RecordingEmailSenderImmediate>());

            // Barındırılan döngü kapalı: testler partiyi kendi OutboxProcessor örnekleriyle deterministik işletir.
            services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        });
    }

    // Dispose sırasında: önce host (ve hosted OutboxProcessor + tüm DbContext bağlantıları) kapatılır,
    // ardından test DB'si güvenli force-drop ile kaldırılır; LocalDB'de stale/bozuk DB veya .mdf/.ldf kalmaz.
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(OutboxProcessorCommandConnection);
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(OutboxProcessorQueryConnection);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            IntegrationTestDatabaseReset.EnsureDroppedAsync(OutboxProcessorCommandConnection).GetAwaiter().GetResult();
            IntegrationTestDatabaseReset.EnsureDroppedAsync(OutboxProcessorQueryConnection).GetAwaiter().GetResult();
        }
    }
}
