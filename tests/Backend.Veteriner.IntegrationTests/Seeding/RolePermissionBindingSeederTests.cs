using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.IntegrationTests.Seeding;

/// <summary>
/// <see cref="RolePermissionBindingSeeder"/> idempotent bağ ekleme garantilerini doğrular:
/// - Admin ve ClinicAdmin rolleri <c>Clinics.Update</c> bağını alır.
/// - Mevcut bağlar korunur, duplicate satır oluşmaz.
/// - Tekrar çalıştırma toplam satır sayısını değiştirmez.
/// Paylaşılan integration DB'ye dokunmamak için ayrı LocalDB veritabanı kullanılır.
/// </summary>
[CollectionDefinition("role-permission-binding-seed", DisableParallelization = true)]
public sealed class RolePermissionBindingSeedCollection;

[Collection("role-permission-binding-seed")]
public sealed class RolePermissionBindingSeederTests
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=VeterinerDb_RolePermissionBindingSeed;Trusted_Connection=True;MultipleActiveResultSets=true";

    private static DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
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

    private sealed class StubBcryptPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) =>
            "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

        public bool Verify(string password, string hash) => true;
    }

    private static async Task<Guid> GetPermissionIdAsync(AppDbContext db, string code) =>
        (await db.Set<Permission>().SingleAsync(p => p.Code == code)).Id;

    private static async Task<Guid> GetClaimIdAsync(AppDbContext db, string name) =>
        (await db.Set<OperationClaim>().SingleAsync(c => c.Name == name)).Id;

    [Fact]
    public async Task Seed_Should_Bind_ClinicsUpdate_To_Admin_And_ClinicAdmin_WithoutDuplicates()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);
        await RolePermissionBindingSeeder.SeedAsync(db);

        var clinicsUpdateId = await GetPermissionIdAsync(db, PermissionCatalog.Clinics.Update);
        var adminClaimId = await GetClaimIdAsync(db, "Admin");
        var clinicAdminClaimId = await GetClaimIdAsync(db, "ClinicAdmin");

        var adminLinks = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == adminClaimId && x.PermissionId == clinicsUpdateId);
        adminLinks.Should().Be(1, "Admin rolü Clinics.Update bağını tam olarak bir kez almalı");

        var clinicAdminLinks = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == clinicAdminClaimId && x.PermissionId == clinicsUpdateId);
        clinicAdminLinks.Should().Be(1, "ClinicAdmin rolü Clinics.Update bağını tam olarak bir kez almalı");

        var remindersReadId = await GetPermissionIdAsync(db, PermissionCatalog.Reminders.Read);
        var remindersManageId = await GetPermissionIdAsync(db, PermissionCatalog.Reminders.Manage);

        var clinicAdminRemindersRead = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == clinicAdminClaimId && x.PermissionId == remindersReadId);
        clinicAdminRemindersRead.Should().Be(1, "ClinicAdmin rolü Reminders.Read bağını almalı");

        var clinicAdminRemindersManage = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == clinicAdminClaimId && x.PermissionId == remindersManageId);
        clinicAdminRemindersManage.Should().Be(1, "ClinicAdmin rolü Reminders.Manage bağını almalı");
    }

    [Fact]
    public async Task Seed_Should_Bind_RemindersReadAndManage_To_Admin_And_Owner()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);

        var ownerClaim = await db.OperationClaims.FirstOrDefaultAsync(x => x.Name == "Owner");
        if (ownerClaim is null)
        {
            ownerClaim = new OperationClaim("Owner");
            await db.OperationClaims.AddAsync(ownerClaim);
            await db.SaveChangesAsync();
        }

        await RolePermissionBindingSeeder.SeedAsync(db);

        var remindersReadId = await GetPermissionIdAsync(db, PermissionCatalog.Reminders.Read);
        var remindersManageId = await GetPermissionIdAsync(db, PermissionCatalog.Reminders.Manage);
        var adminClaimId = await GetClaimIdAsync(db, "Admin");
        var ownerClaimId = await GetClaimIdAsync(db, "Owner");

        var adminRead = await db.OperationClaimPermissions.CountAsync(x =>
            x.OperationClaimId == adminClaimId && x.PermissionId == remindersReadId);
        var adminManage = await db.OperationClaimPermissions.CountAsync(x =>
            x.OperationClaimId == adminClaimId && x.PermissionId == remindersManageId);

        var ownerRead = await db.OperationClaimPermissions.CountAsync(x =>
            x.OperationClaimId == ownerClaimId && x.PermissionId == remindersReadId);
        var ownerManage = await db.OperationClaimPermissions.CountAsync(x =>
            x.OperationClaimId == ownerClaimId && x.PermissionId == remindersManageId);

        adminRead.Should().Be(1);
        adminManage.Should().Be(1);
        ownerRead.Should().Be(1);
        ownerManage.Should().Be(1);
    }

    [Fact]
    public async Task Seed_Should_BeIdempotent_On_RepeatedRuns()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);

        await RolePermissionBindingSeeder.SeedAsync(db);
        var adminClaimId = await GetClaimIdAsync(db, "Admin");
        var clinicAdminClaimId = await GetClaimIdAsync(db, "ClinicAdmin");
        var totalAfterFirstRun = await db.OperationClaimPermissions.CountAsync();
        var adminAfterFirstRun = await db.OperationClaimPermissions.CountAsync(x => x.OperationClaimId == adminClaimId);
        var clinicAdminAfterFirstRun = await db.OperationClaimPermissions.CountAsync(x => x.OperationClaimId == clinicAdminClaimId);

        await RolePermissionBindingSeeder.SeedAsync(db);
        await RolePermissionBindingSeeder.SeedAsync(db);

        var totalAfterRepeatedRuns = await db.OperationClaimPermissions.CountAsync();
        var adminAfterRepeatedRuns = await db.OperationClaimPermissions.CountAsync(x => x.OperationClaimId == adminClaimId);
        var clinicAdminAfterRepeatedRuns = await db.OperationClaimPermissions.CountAsync(x => x.OperationClaimId == clinicAdminClaimId);

        totalAfterRepeatedRuns.Should().Be(totalAfterFirstRun, "tekrar çalıştırma yeni satır üretmemeli");
        adminAfterRepeatedRuns.Should().Be(adminAfterFirstRun);
        clinicAdminAfterRepeatedRuns.Should().Be(clinicAdminAfterFirstRun);
    }

    [Fact]
    public async Task Seed_Should_NotOverride_ExistingLinks_ForClinicAdmin()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);

        var clinicAdminClaimId = await GetClaimIdAsync(db, "ClinicAdmin");
        var clinicsReadId = await GetPermissionIdAsync(db, PermissionCatalog.Clinics.Read);

        await db.OperationClaimPermissions.AddAsync(new OperationClaimPermission(clinicAdminClaimId, clinicsReadId));
        await db.SaveChangesAsync();

        await RolePermissionBindingSeeder.SeedAsync(db);

        var stillHasClinicsRead = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == clinicAdminClaimId && x.PermissionId == clinicsReadId);
        stillHasClinicsRead.Should().BeTrue("mevcut bağ korunmalı — seeder sadece eksik olanı ekler");

        var clinicsUpdateId = await GetPermissionIdAsync(db, PermissionCatalog.Clinics.Update);
        var hasClinicsUpdate = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == clinicAdminClaimId && x.PermissionId == clinicsUpdateId);
        hasClinicsUpdate.Should().BeTrue();
    }
}
