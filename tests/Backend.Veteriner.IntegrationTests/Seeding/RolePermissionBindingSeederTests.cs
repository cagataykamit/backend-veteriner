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
/// - Admin ve ClinicAdmin rolleri <c>Clinics.Update</c> bağını alır; ClinicAdmin ayrıca operasyonel permission’lardan
///   örnek olarak <c>Dashboard.Read</c> alır.
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

        var dashboardReadId = await GetPermissionIdAsync(db, PermissionCatalog.Dashboard.Read);
        var clinicAdminDashboard = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == clinicAdminClaimId && x.PermissionId == dashboardReadId);
        clinicAdminDashboard.Should().Be(1, "ClinicAdmin rolü Dashboard.Read bağını almalı");
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

    // ---------- Faz 4B-6: Admin claim whitelist temizliği ----------

    /// <summary>
    /// Eski AdminClaimSeeder davranışını taklit ederek "Admin" claim'ine sistem permission'larını manuel
    /// bağlar; ardından <see cref="RolePermissionBindingSeeder"/> çalıştığında bu sistem bağlarının
    /// whitelist temizliğiyle silindiğini ve <c>Map["Admin"]</c> içindeki permission'ların korunduğunu doğrular.
    /// </summary>
    [Fact]
    public async Task Seed_Should_Cleanup_NonWhitelisted_System_Permissions_From_Admin_Claim()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);

        var adminClaimId = await GetClaimIdAsync(db, "Admin");

        // Eski davranışı simüle et: tenant Admin claim'ine sistem/whitelist-dışı bağları manuel ekle.
        var systemPermissionCodes = new[]
        {
            PermissionCatalog.Outbox.Read,
            PermissionCatalog.Outbox.Write,
            PermissionCatalog.Roles.Write,
            PermissionCatalog.Permissions.Write,
            PermissionCatalog.Users.Write,
            PermissionCatalog.Admin.Diagnostics,
            PermissionCatalog.Tenants.Create,
            PermissionCatalog.Subscriptions.Manage,
        };

        foreach (var code in systemPermissionCodes)
        {
            var permId = await GetPermissionIdAsync(db, code);
            var alreadyLinked = await db.OperationClaimPermissions
                .AnyAsync(x => x.OperationClaimId == adminClaimId && x.PermissionId == permId);
            if (!alreadyLinked)
            {
                await db.OperationClaimPermissions.AddAsync(new OperationClaimPermission(adminClaimId, permId));
            }
        }
        await db.SaveChangesAsync();

        await RolePermissionBindingSeeder.SeedAsync(db);

        // Whitelist dışı sistem permission'ları temizlenmiş olmalı.
        foreach (var code in systemPermissionCodes)
        {
            var permId = await GetPermissionIdAsync(db, code);
            var hasLink = await db.OperationClaimPermissions
                .AnyAsync(x => x.OperationClaimId == adminClaimId && x.PermissionId == permId);
            hasLink.Should().BeFalse(
                $"'{code}' Map[\"Admin\"] dışında olduğu için tenant Admin claim'inden temizlenmeli");
        }

        // Map["Admin"] içindeki permission'lar korunmalı / eklenmiş olmalı.
        var clinicsUpdateId = await GetPermissionIdAsync(db, PermissionCatalog.Clinics.Update);
        var remindersReadId = await GetPermissionIdAsync(db, PermissionCatalog.Reminders.Read);
        var clientsCreateId = await GetPermissionIdAsync(db, PermissionCatalog.Clients.Create);
        var dashboardReadId = await GetPermissionIdAsync(db, PermissionCatalog.Dashboard.Read);

        (await db.OperationClaimPermissions.AnyAsync(x => x.OperationClaimId == adminClaimId && x.PermissionId == clinicsUpdateId))
            .Should().BeTrue();
        (await db.OperationClaimPermissions.AnyAsync(x => x.OperationClaimId == adminClaimId && x.PermissionId == remindersReadId))
            .Should().BeTrue();
        (await db.OperationClaimPermissions.AnyAsync(x => x.OperationClaimId == adminClaimId && x.PermissionId == clientsCreateId))
            .Should().BeTrue();
        (await db.OperationClaimPermissions.AnyAsync(x => x.OperationClaimId == adminClaimId && x.PermissionId == dashboardReadId))
            .Should().BeTrue();
    }

    /// <summary>
    /// Cleanup yalnız "Admin" claim'i için yapılmalı; ClinicAdmin / Veteriner / Sekreter / Owner
    /// claim'lerinde manuel olarak eklenen whitelist-dışı bağlar korunmalıdır.
    /// </summary>
    [Fact]
    public async Task Seed_Cleanup_Should_Only_Affect_Admin_Claim_Not_Other_Claims()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);

        var clinicAdminClaimId = await GetClaimIdAsync(db, "ClinicAdmin");
        var outboxReadId = await GetPermissionIdAsync(db, PermissionCatalog.Outbox.Read);

        // ClinicAdmin'e manuel olarak Outbox.Read bağı ekle (manual elevation simulation).
        await db.OperationClaimPermissions.AddAsync(new OperationClaimPermission(clinicAdminClaimId, outboxReadId));
        await db.SaveChangesAsync();

        await RolePermissionBindingSeeder.SeedAsync(db);

        var stillHasOutboxRead = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == clinicAdminClaimId && x.PermissionId == outboxReadId);
        stillHasOutboxRead.Should().BeTrue(
            "Cleanup yalnız Admin claim'i içindir; ClinicAdmin manuel atamaları korunmalı");
    }

    /// <summary>
    /// admin@example.com kullanıcısı PlatformAdmin claim'ine bağlı olmalı; tenant Admin claim'i
    /// AdminClaimSeeder tarafından oluşturulmaz, ancak InviteAssignableOperationClaimsSeeder tarafından
    /// boş şekilde oluşturulur. RolePermissionBindingSeeder ise yalnız Map["Admin"] içeriğini bağlar.
    /// </summary>
    [Fact]
    public async Task Seed_Should_Bind_AdminUser_To_PlatformAdmin_And_Keep_TenantAdmin_Limited_To_Whitelist()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await AdminClaimSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);
        await RolePermissionBindingSeeder.SeedAsync(db);

        var adminUser = await db.Users.SingleAsync(u => u.Email == AdminClaimSeeder.PlatformAdminUserEmail);
        var platformAdminClaimId = await GetClaimIdAsync(db, AdminClaimSeeder.PlatformAdminClaimName);
        var tenantAdminClaimId = await GetClaimIdAsync(db, "Admin");

        var hasPlatformLink = await db.UserOperationClaims
            .AnyAsync(x => x.UserId == adminUser.Id && x.OperationClaimId == platformAdminClaimId);
        hasPlatformLink.Should().BeTrue("admin@example.com PlatformAdmin claim'ine bağlı olmalı");

        // PlatformAdmin tüm catalog permission'larını taşır.
        var platformAdminPermCount = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == platformAdminClaimId);
        platformAdminPermCount.Should().Be(PermissionCatalog.All.Length);

        // Tenant Admin claim Map["Admin"] içindeki sayıdan fazla permission almamalı.
        var tenantAdminPermCount = await db.OperationClaimPermissions
            .CountAsync(x => x.OperationClaimId == tenantAdminClaimId);
        var expectedAdminMapSize = RolePermissionBindings.Map["Admin"].Count;
        tenantAdminPermCount.Should().Be(expectedAdminMapSize,
            "Tenant Admin claim yalnız RolePermissionBindings.Map[\"Admin\"] içeriğine sahip olmalı");

        // Tenant Admin claim'inde sistem permission'ları olmamalı.
        var systemCodes = new[]
        {
            PermissionCatalog.Outbox.Read,
            PermissionCatalog.Outbox.Write,
            PermissionCatalog.Admin.Diagnostics,
            PermissionCatalog.Roles.Write,
            PermissionCatalog.Permissions.Write,
            PermissionCatalog.Users.Write,
            PermissionCatalog.Tenants.Create,
            PermissionCatalog.Subscriptions.Manage,
        };
        foreach (var code in systemCodes)
        {
            var permId = await GetPermissionIdAsync(db, code);
            var hasSystemLink = await db.OperationClaimPermissions
                .AnyAsync(x => x.OperationClaimId == tenantAdminClaimId && x.PermissionId == permId);
            hasSystemLink.Should().BeFalse(
                $"Tenant Admin claim '{code}' sistem permission'ını içermemeli (Faz 4B-6 ayrımı)");
        }
    }
}
