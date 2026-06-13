using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Suite başında veritabanını bir kere sıfırlar (EnsureDeleted + Migrate) ve seed eder.
/// Aynı süreçte sonradan boot eden factory örnekleri (her test sınıfı kendi
/// <see cref="CustomWebApplicationFactory"/> örneğini aldığı için) sıfırlamayı tekrar etmez;
/// böylece test verisi koşu boyunca birikmez ve sınıflar arası yarış güvenli kalır.
/// </summary>
internal static class IntegrationTestDatabaseInitializer
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> InitializedDatabases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Verilen veritabanı için (adına göre) tek seferlik reset + migrate + seed çalıştırır.
    /// Çağrıdan önce <see cref="IntegrationTestDatabaseGuard.EnsureSafeDatabase"/> ile doğrulanmış
    /// güvenli bir veritabanı adı beklenir.
    /// </summary>
    public static void EnsureResetMigratedAndSeeded(AppDbContext db, IPasswordHasher hasher, string databaseName)
    {
        Gate.Wait();
        try
        {
            if (InitializedDatabases.Contains(databaseName))
                return;

            // Suite başında bir kere: EnsureDeleted + Migrate.
            IntegrationTestDatabaseReset.ResetAndMigrateAsync(db).GetAwaiter().GetResult();

            // Seed (Production boot ile aynı zincir).
            TestDataSeeder.Seed(db, hasher);
            PermissionSeeder.SeedAsync(db).GetAwaiter().GetResult();
            AdminClaimSeeder.SeedAsync(db).GetAwaiter().GetResult();
            InviteAssignableOperationClaimsSeeder.SeedAsync(db).GetAwaiter().GetResult();

            InitializedDatabases.Add(databaseName);
        }
        finally
        {
            Gate.Release();
        }
    }
}
