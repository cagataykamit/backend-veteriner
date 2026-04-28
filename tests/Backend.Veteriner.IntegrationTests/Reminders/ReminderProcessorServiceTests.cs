using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Reminders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.IntegrationTests.Reminders;

public sealed class ReminderProcessorServiceTests
{
    [Fact]
    public async Task ProcessOnce_Should_NoOp_When_Disabled()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var service = CreateService(db, email, enabled: false);

        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnce_Should_NotSend_When_SettingsMissing()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var tenant = new Tenant("Tenant A");
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnce_Should_Skip_When_EmailChannelDisabled()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var tenant = new Tenant("Tenant A");
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(true, 24, true, 3, false);
        db.TenantReminderSettings.Add(settings);
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnce_Should_Enqueue_AppointmentReminder()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var clinic = new Clinic(tenant.Id, "Klinik", "Istanbul");
        var client = new Client(tenant.Id, "Ali Veli", "905555555555", "ali@example.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "Pamuk", speciesId);
        var appt = new Appointment(tenant.Id, clinic.Id, pet.Id, now.AddHours(24), AppointmentType.Checkup, AppointmentStatus.Scheduled);
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(true, 24, false, 3, true);

        db.AddRange(tenant, clinic, client, pet, appt, settings);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(1);
        var log = await db.ReminderDispatchLogs.SingleAsync();
        log.ReminderType.Should().Be(ReminderType.Appointment);
        log.Status.Should().Be(ReminderDispatchStatus.Enqueued);
    }

    [Fact]
    public async Task ProcessOnce_Should_Enqueue_VaccinationReminder()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var clinic = new Clinic(tenant.Id, "Klinik", "Istanbul");
        var client = new Client(tenant.Id, "Ali Veli", "905555555555", "ali@example.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "Pamuk", speciesId);
        var vaccination = new Vaccination(tenant.Id, pet.Id, clinic.Id, null, "Kuduz", VaccinationStatus.Scheduled, null, now.AddDays(3), null);
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(false, 24, true, 3, true);

        db.AddRange(tenant, clinic, client, pet, vaccination, settings);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(1);
        var log = await db.ReminderDispatchLogs.SingleAsync();
        log.ReminderType.Should().Be(ReminderType.Vaccination);
        log.Status.Should().Be(ReminderDispatchStatus.Enqueued);
    }

    [Fact]
    public async Task ProcessOnce_Should_MarkSkipped_When_EmailMissing()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var clinic = new Clinic(tenant.Id, "Klinik", "Istanbul");
        var client = new Client(tenant.Id, "No Mail");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "Pamuk", speciesId);
        var appt = new Appointment(tenant.Id, clinic.Id, pet.Id, now.AddHours(24), AppointmentType.Checkup, AppointmentStatus.Scheduled);
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(true, 24, false, 3, true);

        db.AddRange(tenant, clinic, client, pet, appt, settings);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(0);
        var log = await db.ReminderDispatchLogs.SingleAsync();
        log.Status.Should().Be(ReminderDispatchStatus.Skipped);
        log.LastError.Should().Contain("missing");
    }

    [Fact]
    public async Task ProcessOnce_Should_NotDuplicate_When_DedupeExists()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var clinic = new Clinic(tenant.Id, "Klinik", "Istanbul");
        var client = new Client(tenant.Id, "Ali Veli", "905555555555", "ali@example.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "Pamuk", speciesId);
        var appt = new Appointment(tenant.Id, clinic.Id, pet.Id, now.AddHours(24), AppointmentType.Checkup, AppointmentStatus.Scheduled);
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(true, 24, false, 3, true);
        var dedupe = $"appointment:{appt.Id:D}:hours-before:24";
        var existing = new ReminderDispatchLog(
            tenant.Id,
            clinic.Id,
            ReminderType.Appointment,
            ReminderSourceEntityType.Appointment,
            appt.Id,
            "ali@example.com",
            "Ali Veli",
            appt.ScheduledAtUtc,
            appt.ScheduledAtUtc.AddHours(-24),
            ReminderDispatchStatus.Enqueued,
            dedupe);

        db.AddRange(tenant, clinic, client, pet, appt, settings, existing);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessOnce_Should_Skip_ReadOnlyTenant()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeEmailSender();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var clinic = new Clinic(tenant.Id, "Klinik", "Istanbul");
        var client = new Client(tenant.Id, "Ali Veli", "905555555555", "ali@example.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "Pamuk", speciesId);
        var appt = new Appointment(tenant.Id, clinic.Id, pet.Id, now.AddHours(24), AppointmentType.Checkup, AppointmentStatus.Scheduled);
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(true, 24, false, 3, true);

        db.AddRange(tenant, clinic, client, pet, appt, settings);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now.AddDays(-40), 7));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Sent.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    private static ReminderProcessorService CreateService(AppDbContext db, FakeEmailSender emailSender, bool enabled = true)
        => new(
            db,
            emailSender,
            Options.Create(new ReminderProcessorOptions
            {
                Enabled = enabled,
                IntervalMinutes = 5,
                BatchSize = 100,
                WindowToleranceMinutes = 10
            }),
            NullLogger<ReminderProcessorService>.Instance);

    private static async Task<AppDbContext> CreateDbContextAsync()
    {
        var dbName = $"VeterinerDb_ReminderProcessor_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static async Task<Guid> GetAnySpeciesIdAsync(AppDbContext db)
        => await db.Species
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => x.Id)
            .FirstAsync();

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject)> Sent { get; } = new();

        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default, bool isHtml = false)
        {
            Sent.Add((to, subject));
            return Task.CompletedTask;
        }
    }
}
