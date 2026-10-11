using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.IntegrationTests.Seeding;

/// <summary>Dev örnek veri seed'i: idempotency, ortam koruması ve sayaç tutarlılığı. Ayrı LocalDB veritabanı kullanır.</summary>
[Collection("admin-seed-orchestration")]
public sealed class DevSampleDataSeederTests
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityCommandDb_DevSampleDataSeed;Trusted_Connection=True;MultipleActiveResultSets=true";

    private static DbContextOptions<AppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .ConfigureWarnings(w => w
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                .Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static OutboxBuffer NewBuffer() => new(NullLogger<OutboxBuffer>.Instance);

    private static async Task<AppDbContext> PrepareAsync(bool withTenant = true)
    {
        var db = new AppDbContext(CreateOptions());
        await IntegrationTestDatabaseReset.ResetAndMigrateAsync(db);
        await SpeciesAndBreedCatalogSeeder.SeedAsync(db);
        if (!withTenant)
            return db;

        var tenant = new Tenant(DevSampleDataSeeder.TenantName);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        db.Clinics.Add(new Clinic(tenant.Id, DevSampleDataSeeder.ClinicName, "Izmir"));
        var user = new User("sample-owner@example.com", "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Seed_Should_Create_Consistent_Finance_Samples_And_Be_Idempotent()
    {
        await using var db = await PrepareAsync();
        var env = new FakeEnvironment(Environments.Development);

        var first = await DevSampleDataSeeder.SeedAsync(db, NewBuffer(), env, ct: default);
        var second = await DevSampleDataSeeder.SeedAsync(db, NewBuffer(), env, ct: default);

        first.Should().NotBeNull();
        first!.Charges.Should().Be(4);
        first.Payments.Should().Be(3);
        first.Appointments.Should().BeInRange(0, 3);
        second.Should().Be(new DevSampleDataSeeder.Summary(0, 0, 0));

        (await db.Charges.CountAsync()).Should().Be(4);
        (await db.Payments.CountAsync()).Should().Be(3);

        // Sayaçlar tahsis satırlarıyla tutarlı olmalı (ham yazım yok, handler/domain yolu).
        var allocations = await db.PaymentAllocations.ToListAsync();
        foreach (var charge in await db.Charges.ToListAsync())
            charge.AllocatedAmount.Should().Be(allocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.Amount));
        foreach (var payment in await db.Payments.ToListAsync())
            payment.AllocatedAmount.Should().Be(allocations.Where(a => a.PaymentId == payment.Id).Sum(a => a.Amount));

        var charges = await db.Charges.ToListAsync();
        charges.Should().OnlyContain(c => c.Description.StartsWith("ÖRNEK"));
        charges.Count(c => c.OutstandingAmount == 0).Should().Be(1);
        charges.Single(c => c.AllocatedAmount == 400m).OutstandingAmount.Should().Be(600m);
        charges.Count(c => c.AllocatedAmount == 0).Should().Be(2);

        var payments = await db.Payments.ToListAsync();
        payments.Count(p => p.RequestId.HasValue && p.RequestHash != null).Should().Be(2);
        payments.Single(p => p.RequestId == null).AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Seed_Should_Create_Past_And_Future_Scheduled_Appointments_Without_Duplicates()
    {
        await using var db = await PrepareAsync();
        var env = new FakeEnvironment(Environments.Development);

        await DevSampleDataSeeder.SeedAsync(db, NewBuffer(), env, ct: default);
        await DevSampleDataSeeder.SeedAsync(db, NewBuffer(), env, ct: default);

        var appointments = await db.Appointments.ToListAsync();
        appointments.Should().OnlyContain(a => a.Status == AppointmentStatus.Scheduled && a.Notes!.StartsWith("ÖRNEK"));
        appointments.Select(a => a.Notes).Should().OnlyHaveUniqueItems();
        appointments.Where(a => a.Notes!.Contains("geçmiş")).Should().OnlyContain(a => a.ScheduledAtUtc <= DateTime.UtcNow);
        appointments.Where(a => a.Notes!.Contains("gelecek")).Should().OnlyContain(a => a.ScheduledAtUtc > DateTime.UtcNow);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Seed_Should_Refuse_To_Run_Outside_Development(string environmentName)
    {
        await using var db = await PrepareAsync();

        var act = () => DevSampleDataSeeder.SeedAsync(db, NewBuffer(), new FakeEnvironment(environmentName));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Charges.AnyAsync()).Should().BeFalse();
        (await db.Payments.AnyAsync()).Should().BeFalse();
        (await db.Clients.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Seed_Should_Skip_Without_Creating_Anything_When_Tenant_Missing()
    {
        await using var db = await PrepareAsync(withTenant: false);

        var summary = await DevSampleDataSeeder.SeedAsync(db, NewBuffer(), new FakeEnvironment(Environments.Development));

        summary.Should().BeNull();
        (await db.Clients.AnyAsync()).Should().BeFalse();
    }
}
