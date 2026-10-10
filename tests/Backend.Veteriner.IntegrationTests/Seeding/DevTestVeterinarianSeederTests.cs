using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.Clinics;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Backend.IntegrationTests.Seeding;

/// <summary>Dev test hekim seed'i: idempotency, ortam koruması ve hekim kuralıyla uyum. Ayrı LocalDB veritabanı kullanır.</summary>
[Collection("admin-seed-orchestration")]
public sealed class DevTestVeterinarianSeederTests
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityCommandDb_DevTestVeterinarianSeed;Trusted_Connection=True;MultipleActiveResultSets=true";

    private static DbContextOptions<AppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .ConfigureWarnings(w => w
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                .Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    private sealed class StubBcryptPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) =>
            "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

        public bool Verify(string password, string hash) => true;
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<(AppDbContext Db, Guid TenantId, Guid ClinicId)> PrepareAsync()
    {
        var db = new AppDbContext(CreateOptions());
        await IntegrationTestDatabaseReset.ResetAndMigrateAsync(db);
        var hasher = new StubBcryptPasswordHasher();
        await PermissionSeeder.SeedAsync(db);
        await DataSeeder.SeedAsync(db, hasher);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);

        var tenant = new Tenant(DevTestVeterinarianSeeder.TenantName);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var clinic = new Clinic(tenant.Id, DevTestVeterinarianSeeder.ClinicName, "Izmir");
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();
        return (db, tenant.Id, clinic.Id);
    }

    [Fact]
    public async Task Seed_Should_Create_Confirmed_Veterinarian_Assigned_To_Clinic_And_Be_Idempotent()
    {
        var (db, tenantId, clinicId) = await PrepareAsync();
        await using var _ = db;
        var env = new FakeEnvironment(Environments.Development);

        (await DevTestVeterinarianSeeder.SeedAsync(db, new StubBcryptPasswordHasher(), env)).Should().BeTrue();
        (await DevTestVeterinarianSeeder.SeedAsync(db, new StubBcryptPasswordHasher(), env)).Should().BeTrue();

        var users = await db.Users.Where(u => u.Email == DevTestVeterinarianSeeder.Email).ToListAsync();
        users.Should().ContainSingle();
        var user = users[0];
        user.EmailConfirmed.Should().BeTrue();
        user.DisplayName.Should().Be(DevTestVeterinarianSeeder.DisplayName);
        (await db.UserTenants.CountAsync(x => x.UserId == user.Id && x.TenantId == tenantId)).Should().Be(1);
        (await db.UserClinics.CountAsync(x => x.UserId == user.Id && x.ClinicId == clinicId)).Should().Be(1);
        (await db.UserOperationClaims.CountAsync(x => x.UserId == user.Id)).Should().Be(1);

        var reader = new ClinicVeterinarianReader(db);
        (await reader.IsClinicVeterinarianAsync(user.Id, tenantId, clinicId, CancellationToken.None)).Should().BeTrue();
        var listed = await reader.ListAsync(tenantId, clinicId, CancellationToken.None);
        listed.Should().ContainSingle(v => v.UserId == user.Id && v.Name == DevTestVeterinarianSeeder.DisplayName);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Seed_Should_Refuse_To_Run_Outside_Development(string environmentName)
    {
        var (db, _, _) = await PrepareAsync();
        await using var _ = db;

        var act = () => DevTestVeterinarianSeeder.SeedAsync(
            db, new StubBcryptPasswordHasher(), new FakeEnvironment(environmentName));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Users.AnyAsync(u => u.Email == DevTestVeterinarianSeeder.Email)).Should().BeFalse();
    }

    [Fact]
    public async Task Seed_Should_Skip_Without_Creating_Anything_When_Tenant_Or_Clinic_Missing()
    {
        await using var db = new AppDbContext(CreateOptions());
        await IntegrationTestDatabaseReset.ResetAndMigrateAsync(db);
        await PermissionSeeder.SeedAsync(db);
        await InviteAssignableOperationClaimsSeeder.SeedAsync(db);

        var ready = await DevTestVeterinarianSeeder.SeedAsync(
            db, new StubBcryptPasswordHasher(), new FakeEnvironment(Environments.Development));

        ready.Should().BeFalse();
        (await db.Users.AnyAsync(u => u.Email == DevTestVeterinarianSeeder.Email)).Should().BeFalse();
    }
}
