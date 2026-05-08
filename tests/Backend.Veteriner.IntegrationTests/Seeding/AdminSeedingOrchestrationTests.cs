using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.IntegrationTests.Seeding;

/// <summary>
/// Production boot sırası: <c>PermissionSeeder → DataSeeder → AdminClaimSeeder</c>.
/// Faz 4B-6 sonrası <see cref="AdminClaimSeeder"/> "Admin" claim'i yerine "PlatformAdmin" claim'i oluşturur,
/// admin@example.com kullanıcısını PlatformAdmin'e bağlar ve tüm <see cref="PermissionCatalog"/> permission'larını
/// PlatformAdmin claim'ine bağlar. Tenant Admin claim'i bu seeder tarafından oluşturulmaz; o
/// <see cref="InviteAssignableOperationClaimsSeeder"/> sorumluluğundadır.
/// Paylaşılan integration DB'ye dokunmamak için ayrı LocalDB veritabanı kullanılır.
/// </summary>
[CollectionDefinition("admin-seed-orchestration", DisableParallelization = true)]
public sealed class AdminSeedOrchestrationCollection;

[Collection("admin-seed-orchestration")]
public sealed class AdminSeedingOrchestrationTests
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=VeterinerDb_AdminSeedOrchestration;Trusted_Connection=True;MultipleActiveResultSets=true";

    private static DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            // Seed testinde outbox interceptor'ları devre dışı (yalın DbContext)
            .ConfigureWarnings(w => w
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                .Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
    }

    private static async Task ResetDatabaseAsync(AppDbContext db)
    {
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Bcrypt formatında hash üretir; DataSeeder'ın format kontrolünü geçer.
    /// </summary>
    private sealed class StubBcryptPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) =>
            "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

        public bool Verify(string password, string hash) => true;
    }

    [Fact]
    public async Task ProductionOrder_FirstBoot_Should_CreatePlatformAdminUserClaimAndAllPermissionLinks()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);

        var adminUser = await db.Users.SingleOrDefaultAsync(u => u.Email == AdminClaimSeeder.PlatformAdminUserEmail);
        adminUser.Should().NotBeNull();

        var platformAdminClaim = await db.OperationClaims.SingleOrDefaultAsync(c => c.Name == AdminClaimSeeder.PlatformAdminClaimName);
        platformAdminClaim.Should().NotBeNull("AdminClaimSeeder PlatformAdmin claim'i oluşturmalı");

        var hasUserClaim = await db.UserOperationClaims.AnyAsync(
            x => x.UserId == adminUser!.Id && x.OperationClaimId == platformAdminClaim!.Id);
        hasUserClaim.Should().BeTrue("admin@example.com PlatformAdmin claim'ine bağlı olmalı");

        var permCount = await db.Permissions.CountAsync();
        permCount.Should().Be(PermissionCatalog.All.Length);

        var linkedCount = await db.OperationClaimPermissions.CountAsync(
            x => x.OperationClaimId == platformAdminClaim!.Id);
        linkedCount.Should().Be(permCount, "PlatformAdmin claim tüm catalog permission'larına bağlı olmalı");

        // Faz 4B-6: AdminClaimSeeder artık "Admin" tenant claim'i oluşturmaz.
        var tenantAdminClaim = await db.OperationClaims.SingleOrDefaultAsync(c => c.Name == "Admin");
        tenantAdminClaim.Should().BeNull(
            "AdminClaimSeeder yalnızca PlatformAdmin oluşturmalı; tenant Admin claim'i InviteAssignableOperationClaimsSeeder sorumluluğunda");
    }

    [Fact]
    public async Task WrongOrder_AdminClaimSeederBeforeDataSeeder_Should_LeavePlatformAdminClaimWithoutUserBinding()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await AdminClaimSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);

        var adminUser = await db.Users.SingleOrDefaultAsync(u => u.Email == AdminClaimSeeder.PlatformAdminUserEmail);
        adminUser.Should().NotBeNull();

        var platformAdminClaim = await db.OperationClaims.SingleOrDefaultAsync(c => c.Name == AdminClaimSeeder.PlatformAdminClaimName);
        platformAdminClaim.Should().NotBeNull("PlatformAdmin claim hâlâ oluşturulmalı (kullanıcıdan bağımsız)");

        var hasUserClaim = await db.UserOperationClaims.AnyAsync(
            x => x.UserId == adminUser!.Id && x.OperationClaimId == platformAdminClaim!.Id);
        hasUserClaim.Should().BeFalse("AdminClaimSeeder kullanıcı yokken çıktığı için atama yapılmamalı");

        var permCount = await db.Permissions.CountAsync();
        permCount.Should().Be(PermissionCatalog.All.Length);

        var linkedCount = await db.OperationClaimPermissions.CountAsync(
            x => x.OperationClaimId == platformAdminClaim!.Id);
        linkedCount.Should().Be(permCount,
            "PlatformAdmin claim tüm permission'lara bağlanmış olmalı (kullanıcı atama atlansa da claim seed'i çalışır)");
    }

    [Fact]
    public async Task SecondRun_Should_BeIdempotent_For_PlatformAdminBindings()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);

        var platformAdminClaim = await db.OperationClaims.SingleAsync(c => c.Name == AdminClaimSeeder.PlatformAdminClaimName);
        var firstRunCount = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == platformAdminClaim.Id);

        await AdminClaimSeeder.SeedAsync(db);
        await AdminClaimSeeder.SeedAsync(db);

        var repeatedCount = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == platformAdminClaim.Id);

        repeatedCount.Should().Be(firstRunCount, "AdminClaimSeeder idempotent olmalı; tekrar çalıştırma yeni satır üretmemeli");
    }
}
