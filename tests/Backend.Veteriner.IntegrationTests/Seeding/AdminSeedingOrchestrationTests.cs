using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.IntegrationTests.Seeding;

/// <summary>
/// Production boot sırası: PermissionSeeder → DataSeeder → AdminClaimSeeder.
/// Paylaşılan integration DB'ye dokunmamak için ayrı LocalDB veritabanı kullanılır.
/// </summary>
[CollectionDefinition("admin-seed-orchestration", DisableParallelization = true)]
public sealed class AdminSeedOrchestrationCollection;

[Collection("admin-seed-orchestration")]
public sealed class AdminSeedingOrchestrationTests
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=Backend_Veteriner_AdminSeedOrchestration;Trusted_Connection=True;MultipleActiveResultSets=true";

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
    public async Task ProductionOrder_FirstBoot_Should_CreateAdminUserClaimAndAllPermissionLinks()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);

        var adminUser = await db.Users.SingleOrDefaultAsync(u => u.Email == "admin@example.com");
        adminUser.Should().NotBeNull();

        var adminClaim = await db.OperationClaims.SingleOrDefaultAsync(c => c.Name == "Admin");
        adminClaim.Should().NotBeNull();

        var hasUserClaim = await db.UserOperationClaims.AnyAsync(
            x => x.UserId == adminUser!.Id && x.OperationClaimId == adminClaim!.Id);
        hasUserClaim.Should().BeTrue();

        var permCount = await db.Permissions.CountAsync();
        permCount.Should().Be(PermissionCatalog.All.Length);

        var linkedCount = await db.OperationClaimPermissions.CountAsync(
            x => x.OperationClaimId == adminClaim!.Id);
        linkedCount.Should().Be(permCount);
    }

    [Fact]
    public async Task WrongOrder_AdminClaimSeederBeforeDataSeeder_Should_LeaveAdminWithoutClaimAndPermissionLinks()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await AdminClaimSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);

        var adminUser = await db.Users.SingleOrDefaultAsync(u => u.Email == "admin@example.com");
        adminUser.Should().NotBeNull();

        var adminClaim = await db.OperationClaims.SingleOrDefaultAsync(c => c.Name == "Admin");
        adminClaim.Should().NotBeNull();

        var hasUserClaim = await db.UserOperationClaims.AnyAsync(
            x => x.UserId == adminUser!.Id && x.OperationClaimId == adminClaim!.Id);
        hasUserClaim.Should().BeFalse("AdminClaimSeeder kullanıcı yokken çıktığı için atama yapılmamalı");

        var linkedCount = await db.OperationClaimPermissions.CountAsync(
            x => x.OperationClaimId == adminClaim!.Id);
        linkedCount.Should().Be(0);
    }
}
