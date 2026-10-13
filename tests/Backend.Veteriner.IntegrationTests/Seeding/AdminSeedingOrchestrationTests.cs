using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Tenants;
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
    private static readonly string ConnectionString =
        IntegrationTestDatabaseNames.ConnectionString(IntegrationTestDatabaseNames.Command("AdminSeedOrchestration"));

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

    private static Task ResetDatabaseAsync(AppDbContext db)
        => IntegrationTestDatabaseReset.ResetAndMigrateAsync(db);

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

    [Fact]
    public async Task ProductionSeed_Should_NotCreateDefaultAdminTenantOrLink_When_DefaultAdminDisabled()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, new StubBcryptPasswordHasher(), seedDefaultAdmin: false);
        await AdminClaimSeeder.SeedAsync(db, platformAdminEmail: null);

        (await db.Users.AnyAsync(u => u.Email == AdminClaimSeeder.PlatformAdminUserEmail)).Should().BeFalse();
        (await db.Tenants.AnyAsync(t => t.Name == DataSeeder.DefaultTenantName)).Should().BeFalse();
        (await db.OperationClaims.AnyAsync(c => c.Name == AdminClaimSeeder.PlatformAdminClaimName)).Should().BeTrue();
    }

    [Fact]
    public async Task ProductionSeed_Should_NotGrantPlatformAdmin_To_PreexistingDefaultEmailUser()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);
        await PermissionSeeder.SeedAsync(db);
        db.Users.Add(new Backend.Veteriner.Domain.Users.User(AdminClaimSeeder.PlatformAdminUserEmail, new StubBcryptPasswordHasher().Hash("x")));
        await db.SaveChangesAsync();

        await AdminClaimSeeder.SeedAsync(db, platformAdminEmail: null);

        (await db.UserOperationClaims.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PlatformAdminBootstrapper_Should_CreateUserLinkedToPlatformAdmin_And_RefuseDuplicateOrWeakPassword()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);
        var hasher = new StubBcryptPasswordHasher();
        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher, seedDefaultAdmin: false);
        await AdminClaimSeeder.SeedAsync(db, platformAdminEmail: null);

        var weak = () => PlatformAdminBootstrapper.CreateAsync(db, hasher, new("owner@vetinity.test", "zayif"));
        await weak.Should().ThrowAsync<InvalidOperationException>();

        await PlatformAdminBootstrapper.CreateAsync(db, hasher, new("Owner@Vetinity.test", "Guclu-Parola-9!"));

        var user = await db.Users.SingleAsync(u => u.Email == "owner@vetinity.test");
        var claim = await db.OperationClaims.SingleAsync(c => c.Name == AdminClaimSeeder.PlatformAdminClaimName);
        (await db.UserOperationClaims.AnyAsync(x => x.UserId == user.Id && x.OperationClaimId == claim.Id)).Should().BeTrue();
        (await db.UserTenants.CountAsync(x => x.UserId == user.Id)).Should().Be(1);

        var platformTenant = await db.Tenants.SingleAsync(t => t.Name == Tenant.PlatformTenantName);
        platformTenant.IsActive.Should().BeTrue();
        (await db.UserTenants.AnyAsync(x => x.UserId == user.Id && x.TenantId == platformTenant.Id)).Should().BeTrue();
        (await db.Tenants.AnyAsync(t => t.Name == DataSeeder.DefaultTenantName)).Should().BeFalse();
        (await db.Clinics.AnyAsync()).Should().BeFalse();
        (await db.TenantSubscriptions.AnyAsync()).Should().BeFalse();

        var duplicate = () => PlatformAdminBootstrapper.CreateAsync(db, hasher, new("owner@vetinity.test", "Guclu-Parola-9!"));
        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*zaten var*");
    }

    [Fact]
    public async Task PlatformAdminBootstrapper_Should_ReusePlatformTenant_For_SecondAdmin()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);
        var hasher = new StubBcryptPasswordHasher();
        await PermissionSeeder.SeedAsync(db);
        await AdminClaimSeeder.SeedAsync(db, platformAdminEmail: null);

        await PlatformAdminBootstrapper.CreateAsync(db, hasher, new("ilk@vetinity.test", "Guclu-Parola-9!"));
        await PlatformAdminBootstrapper.CreateAsync(db, hasher, new("ikinci@vetinity.test", "Guclu-Parola-9!"));

        (await db.Tenants.CountAsync(t => t.Name == Tenant.PlatformTenantName)).Should().Be(1);
        (await db.UserTenants.Select(x => x.TenantId).Distinct().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PlatformAdminBootstrapper_Should_Refuse_When_PlatformNamedTenantHasClinic()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);
        var hasher = new StubBcryptPasswordHasher();
        await PermissionSeeder.SeedAsync(db);
        await AdminClaimSeeder.SeedAsync(db, platformAdminEmail: null);
        var customerTenant = new Tenant(Tenant.PlatformTenantName);
        db.Tenants.Add(customerTenant);
        db.Clinics.Add(new Backend.Veteriner.Domain.Clinics.Clinic(customerTenant.Id, "Müşteri Kliniği", "İstanbul"));
        await db.SaveChangesAsync();

        var act = () => PlatformAdminBootstrapper.CreateAsync(db, hasher, new("owner@vetinity.test", "Guclu-Parola-9!"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*müşteri kiracısı*");
        (await db.Users.AnyAsync(u => u.Email == "owner@vetinity.test")).Should().BeFalse();
    }
}
