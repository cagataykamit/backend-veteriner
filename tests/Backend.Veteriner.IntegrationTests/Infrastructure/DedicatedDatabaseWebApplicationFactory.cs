using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Paylaşılan suite veritabanı (<see cref="CustomWebApplicationFactory"/>) yerine ayrı command/query LocalDB veritabanıyla
/// çalışan fabrikaların ortak iskeleti (tek yer):
/// - Veritabanı adları koşu kimliklidir (<see cref="IntegrationTestDatabaseNames"/>); bağlantı dizeleri hem config hem servis
///   seviyesinde sabitlenir (ortam değişkeni ezemez).
/// - Bağlantı açılmadan önce efektif ad güvenlik kapısından geçer (<see cref="IntegrationTestDatabaseGuard"/>).
/// - Command DB host kurulurken düşürülüp yeniden oluşturulur ve tohumlanır; query DB sıfırlanıp göç ettirilir.
/// - Kapanışta host durdurulduktan sonra iki veritabanı da düşürülür.
/// - Gerçek SMTP çağrılmaz (<see cref="NoOpEmailSenderImmediate"/>).
/// Türevler yalnız ayar ve servis farkını verir.
/// </summary>
public abstract class DedicatedDatabaseWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private readonly bool _ownsQueryDatabase;

    /// <param name="databaseKey">Command/query veritabanı adlarının taban anahtarı (aynı anahtarı kullanan fabrikalar veritabanını paylaşır).</param>
    /// <param name="invalidQueryConnection">
    /// Verilirse query bağlantısı bilerek geçersizdir (sağlık/hata yolu testleri); query DB kurulmaz ve düşürülmez.
    /// </param>
    protected DedicatedDatabaseWebApplicationFactory(string databaseKey, string? invalidQueryConnection = null)
    {
        CommandDatabaseName = IntegrationTestDatabaseNames.Command(databaseKey);
        QueryDatabaseName = IntegrationTestDatabaseNames.Query(databaseKey);
        CommandConnection = IntegrationTestDatabaseNames.ConnectionString(CommandDatabaseName);
        QueryConnection = invalidQueryConnection ?? IntegrationTestDatabaseNames.ConnectionString(QueryDatabaseName);
        _ownsQueryDatabase = invalidQueryConnection is null;
    }

    public string CommandDatabaseName { get; }

    public string QueryDatabaseName { get; }

    public string CommandConnection { get; }

    public string QueryConnection { get; }

    /// <summary>Rol, izin ve talep tohumları (yetkili uç çağıran testler); yalnız temel tohum gerekenler kapatır.</summary>
    protected virtual bool SeedAuthorization => true;

    /// <summary>Bağlantı dizelerine ve hız sınırı kapatmaya eklenen uygulama ayarları.</summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> Settings => [];

    /// <summary>Servis farkları (seçenekler, sahte bağımlılıklar); ortak kayıtlardan sonra çalışır.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = CommandConnection,
                ["ConnectionStrings:SqlServer"] = CommandConnection,
                ["ConnectionStrings:QueryConnection"] = QueryConnection
            }
            .Concat(Settings)
            .Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });

        builder.ConfigureServices(services =>
        {
            IntegrationTestDbContextOverride.UseDedicatedDatabase(services, CommandConnection);
            IntegrationTestDbContextOverride.UseDedicatedQueryDatabase(services, QueryConnection);

            using var scope = services.BuildServiceProvider().CreateSystemScope();
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                commandDb.Database.GetConnectionString(),
                allowedPrefix: CommandDatabaseName);

            QueryDbContext? queryDb = null;
            if (_ownsQueryDatabase)
            {
                queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
                IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                    queryDb.Database.GetConnectionString(),
                    allowedPrefix: QueryDatabaseName);
            }

            // EnsureCreated LocalDB auto-close/stale durumda "already exists" üretebiliyor: hosted servisler ve DbContext
            // bağlantıları açılmadan önce güvenli reset ile eski veritabanı düşürülür, şema temiz kurulur.
            IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
            commandDb.Database.EnsureCreated();
            TestDataSeeder.Seed(commandDb, hasher);
            if (SeedAuthorization)
            {
                PermissionSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
                AdminClaimSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
                InviteAssignableOperationClaimsSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
                RolePermissionBindingSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            }

            if (queryDb is not null)
                IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb).GetAwaiter().GetResult();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            ConfigureTestServices(services);
        });
    }

    // Önce host (hosted servisler + tüm DbContext bağlantıları) kapanır, ardından test veritabanları güvenli force-drop ile
    // kaldırılır; LocalDB'de stale veritabanı veya .mdf/.ldf kalmaz. Düşürme hatası koşuyu kırmaz (bkz. TryEnsureDroppedAsync).
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await IntegrationTestDatabaseReset.TryEnsureDroppedAsync(CommandConnection);
        if (_ownsQueryDatabase)
            await IntegrationTestDatabaseReset.TryEnsureDroppedAsync(QueryConnection);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            IntegrationTestDatabaseReset.TryEnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
            if (_ownsQueryDatabase)
                IntegrationTestDatabaseReset.TryEnsureDroppedAsync(QueryConnection).GetAwaiter().GetResult();
        }
    }
}
