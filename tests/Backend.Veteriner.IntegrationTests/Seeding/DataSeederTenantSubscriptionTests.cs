using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.IntegrationTests.Seeding;

/// <summary>
/// <see cref="DataSeeder"/> varsayılan kiracı için <see cref="TenantSubscription"/> oluşturma ve idempotency.
/// </summary>
[CollectionDefinition("data-seeder-tenant-subscription", DisableParallelization = true)]
public sealed class DataSeederTenantSubscriptionSeedCollection;

[Collection("data-seeder-tenant-subscription")]
public sealed class DataSeederTenantSubscriptionTests
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=VeterinerDb_DataSeederTenantSubscription;Trusted_Connection=True;MultipleActiveResultSets=true";

    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .ConfigureWarnings(w => w
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                .Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    private static Task ResetDatabaseAsync(AppDbContext db)
        => IntegrationTestDatabaseReset.ResetAndMigrateAsync(db);

    private sealed class StubBcryptPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) =>
            "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

        public bool Verify(string password, string hash) => true;
    }

    [Fact]
    public async Task SeedAsync_Should_Create_Active_Premium_Subscription_For_Default_Tenant()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();
        await DataSeeder.SeedAsync(db, hasher);

        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var sub = await db.TenantSubscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);

        sub.PlanCode.Should().Be(SubscriptionPlanCode.Premium);
        sub.Status.Should().Be(TenantSubscriptionStatus.Active);
        sub.ActivatedAtUtc.Should().NotBeNull();
        sub.CancelledAtUtc.Should().BeNull();
        sub.TrialStartsAtUtc.Should().NotBeNull();
        sub.TrialEndsAtUtc.Should().NotBeNull();
        sub.TrialEndsAtUtc!.Value.Should().BeAfter(sub.TrialStartsAtUtc!.Value);
        sub.CreatedAtUtc.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
        sub.UpdatedAtUtc.Should().NotBeNull();

        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, DateTime.UtcNow);
        effective.Should().Be(TenantSubscriptionStatus.Active);
        TenantSubscriptionEffectiveWriteEvaluator.WriteAllowed(effective).Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_Twice_Should_Not_Duplicate_TenantSubscription()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();
        await DataSeeder.SeedAsync(db, hasher);
        await DataSeeder.SeedAsync(db, hasher);

        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var count = await db.TenantSubscriptions.CountAsync(s => s.TenantId == tenant.Id);
        count.Should().Be(1);
    }
}
