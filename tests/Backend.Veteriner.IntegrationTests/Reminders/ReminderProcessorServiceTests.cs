using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
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
        var email = new FakeReminderEmailOutboxEnqueuer();
        var service = CreateService(db, email, enabled: false);

        await service.ProcessOnceAsync(CancellationToken.None);

        email.Enqueued.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnce_Should_NotSend_When_SettingsMissing()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var tenant = new Tenant("Tenant A");
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Enqueued.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnce_Should_Skip_When_EmailChannelDisabled()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var tenant = new Tenant("Tenant A");
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(true, 24, true, 3, false);
        db.TenantReminderSettings.Add(settings);
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        email.Enqueued.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnce_Should_Enqueue_AppointmentReminder()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
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

        email.Enqueued.Count.Should().Be(1);
        var log = await db.ReminderDispatchLogs.SingleAsync();
        log.ReminderType.Should().Be(ReminderType.Appointment);
        log.Status.Should().Be(ReminderDispatchStatus.Enqueued);
        log.OutboxMessageId.Should().NotBeNull();
        (await db.OutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessOnce_Should_Enqueue_VaccinationReminder()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
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

        email.Enqueued.Count.Should().Be(1);
        var log = await db.ReminderDispatchLogs.SingleAsync();
        log.ReminderType.Should().Be(ReminderType.Vaccination);
        log.Status.Should().Be(ReminderDispatchStatus.Enqueued);
        log.OutboxMessageId.Should().NotBeNull();
        (await db.OutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessOnce_Should_MarkSkipped_When_EmailMissing()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
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

        email.Enqueued.Count.Should().Be(0);
        var log = await db.ReminderDispatchLogs.SingleAsync();
        log.Status.Should().Be(ReminderDispatchStatus.Skipped);
        log.LastError.Should().Contain("missing");
    }

    [Fact]
    public async Task ProcessOnce_Should_NotDuplicate_When_DedupeExists()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
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

        email.Enqueued.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessOnce_Should_Skip_ReadOnlyTenant()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
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

        email.Enqueued.Count.Should().Be(0);
        (await db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnce_Should_MarkSent_When_OutboxProcessed()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(false, 24, false, 3, true);

        var outboxId = Guid.NewGuid();
        var log = new ReminderDispatchLog(
            tenant.Id,
            null,
            ReminderType.Appointment,
            ReminderSourceEntityType.Appointment,
            Guid.NewGuid(),
            "ali@example.com",
            "Ali",
            now.AddHours(1),
            now,
            ReminderDispatchStatus.Enqueued,
            $"dedupe:{Guid.NewGuid():N}");
        log.MarkEnqueued(outboxId);

        db.AddRange(tenant, settings, log);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            Type = "Email",
            Payload = "{}",
            CreatedAtUtc = now.AddMinutes(-10),
            ProcessedAtUtc = now
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        var updated = await db.ReminderDispatchLogs.SingleAsync();
        updated.Status.Should().Be(ReminderDispatchStatus.Sent);
        updated.SentAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task ProcessOnce_Should_MarkFailed_When_OutboxDeadLettered()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(false, 24, false, 3, true);

        var outboxId = Guid.NewGuid();
        var log = new ReminderDispatchLog(
            tenant.Id,
            null,
            ReminderType.Vaccination,
            ReminderSourceEntityType.Vaccination,
            Guid.NewGuid(),
            "ali@example.com",
            "Ali",
            now.AddHours(1),
            now,
            ReminderDispatchStatus.Enqueued,
            $"dedupe:{Guid.NewGuid():N}");
        log.MarkEnqueued(outboxId);

        db.AddRange(tenant, settings, log);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            Type = "Email",
            Payload = "{}",
            CreatedAtUtc = now.AddMinutes(-10),
            DeadLetterAtUtc = now,
            LastError = "535 BadCredentials"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        var updated = await db.ReminderDispatchLogs.SingleAsync();
        updated.Status.Should().Be(ReminderDispatchStatus.Failed);
        updated.FailedAtUtc.Should().Be(now);
        updated.LastError.Should().Contain("535");
    }

    [Fact]
    public async Task ProcessOnce_Should_KeepEnqueued_When_OutboxInRetry()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(false, 24, false, 3, true);

        var outboxId = Guid.NewGuid();
        var log = new ReminderDispatchLog(
            tenant.Id,
            null,
            ReminderType.Appointment,
            ReminderSourceEntityType.Appointment,
            Guid.NewGuid(),
            "ali@example.com",
            "Ali",
            now.AddHours(1),
            now,
            ReminderDispatchStatus.Enqueued,
            $"dedupe:{Guid.NewGuid():N}");
        log.MarkEnqueued(outboxId);

        db.AddRange(tenant, settings, log);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            Type = "Email",
            Payload = "{}",
            CreatedAtUtc = now.AddMinutes(-10),
            RetryCount = 2,
            NextAttemptAtUtc = now.AddMinutes(5)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        var updated = await db.ReminderDispatchLogs.SingleAsync();
        updated.Status.Should().Be(ReminderDispatchStatus.Enqueued);
    }

    [Fact]
    public async Task ProcessOnce_Should_KeepEnqueued_When_OutboxMessageIdNullAndRecent()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(false, 24, false, 3, true);

        var log = new ReminderDispatchLog(
            tenant.Id,
            null,
            ReminderType.Appointment,
            ReminderSourceEntityType.Appointment,
            Guid.NewGuid(),
            "ali@example.com",
            "Ali",
            now.AddHours(1),
            now,
            ReminderDispatchStatus.Enqueued,
            $"dedupe:{Guid.NewGuid():N}");

        db.AddRange(tenant, settings, log);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        var updated = await db.ReminderDispatchLogs.SingleAsync();
        updated.Status.Should().Be(ReminderDispatchStatus.Enqueued);
        updated.SentAtUtc.Should().BeNull();
        updated.FailedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ProcessOnce_Should_MarkFailed_When_OutboxMessageIdNullAndStale()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var now = DateTime.UtcNow;
        var tenant = new Tenant("Tenant A");
        var settings = TenantReminderSettings.CreateDefault(tenant.Id);
        settings.Update(false, 24, false, 3, true);

        var log = new ReminderDispatchLog(
            tenant.Id,
            null,
            ReminderType.Appointment,
            ReminderSourceEntityType.Appointment,
            Guid.NewGuid(),
            "ali@example.com",
            "Ali",
            now.AddHours(1),
            now,
            ReminderDispatchStatus.Enqueued,
            $"dedupe:{Guid.NewGuid():N}");

        db.AddRange(tenant, settings, log);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));
        await db.SaveChangesAsync();

        db.Entry(log).Property(x => x.CreatedAtUtc).CurrentValue = now.AddMinutes(-45);
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        var updated = await db.ReminderDispatchLogs.SingleAsync();
        updated.Status.Should().Be(ReminderDispatchStatus.Failed);
        updated.LastError.Should().Contain("Gönderim sonucu doğrulanamadı");
        updated.FailedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessOnce_Should_NotUpdateOtherTenantLog()
    {
        await using var db = await CreateDbContextAsync();
        var email = new FakeReminderEmailOutboxEnqueuer();
        var now = DateTime.UtcNow;
        var tenantA = new Tenant("Tenant A");
        var tenantB = new Tenant("Tenant B");
        var settingsA = TenantReminderSettings.CreateDefault(tenantA.Id);
        settingsA.Update(false, 24, false, 3, true);
        var settingsB = TenantReminderSettings.CreateDefault(tenantB.Id);
        settingsB.Update(false, 24, false, 3, true);

        var outboxA = Guid.NewGuid();
        var outboxB = Guid.NewGuid();
        var logA = new ReminderDispatchLog(
            tenantA.Id, null, ReminderType.Appointment, ReminderSourceEntityType.Appointment, Guid.NewGuid(),
            "a@example.com", "A", now.AddHours(1), now, ReminderDispatchStatus.Enqueued, $"dedupe:{Guid.NewGuid():N}");
        logA.MarkEnqueued(outboxA);
        var logB = new ReminderDispatchLog(
            tenantB.Id, null, ReminderType.Appointment, ReminderSourceEntityType.Appointment, Guid.NewGuid(),
            "b@example.com", "B", now.AddHours(1), now, ReminderDispatchStatus.Enqueued, $"dedupe:{Guid.NewGuid():N}");
        logB.MarkEnqueued(outboxB);

        db.AddRange(tenantA, tenantB, settingsA, settingsB, logA, logB);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenantA.Id, SubscriptionPlanCode.Basic, now, 14));
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenantB.Id, SubscriptionPlanCode.Basic, now, 14));
        db.OutboxMessages.Add(new OutboxMessage { Id = outboxA, Type = "Email", Payload = "{}", CreatedAtUtc = now, ProcessedAtUtc = now });
        db.OutboxMessages.Add(new OutboxMessage { Id = outboxB, Type = "Email", Payload = "{}", CreatedAtUtc = now, RetryCount = 1, NextAttemptAtUtc = now.AddMinutes(2) });
        await db.SaveChangesAsync();

        var service = CreateService(db, email);
        await service.ProcessOnceAsync(CancellationToken.None);

        var updatedA = await db.ReminderDispatchLogs.SingleAsync(x => x.TenantId == tenantA.Id);
        var updatedB = await db.ReminderDispatchLogs.SingleAsync(x => x.TenantId == tenantB.Id);
        updatedA.Status.Should().Be(ReminderDispatchStatus.Sent);
        updatedB.Status.Should().Be(ReminderDispatchStatus.Enqueued);
    }

    private static ReminderProcessorService CreateService(AppDbContext db, FakeReminderEmailOutboxEnqueuer enqueuer, bool enabled = true)
    {
        enqueuer.Db = db;
        return new ReminderProcessorService(
            db,
            enqueuer,
            Options.Create(new ReminderProcessorOptions
            {
                Enabled = enabled,
                IntervalMinutes = 5,
                BatchSize = 100,
                WindowToleranceMinutes = 10
            }),
            NullLogger<ReminderProcessorService>.Instance);
    }

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

    private sealed class FakeReminderEmailOutboxEnqueuer : IReminderEmailOutboxEnqueuer
    {
        public List<(Guid Id, string To, string Subject)> Enqueued { get; } = new();

        public Task<Guid> EnqueueReminderEmailAsync(string to, string subject, string body, bool isHtml, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            Enqueued.Add((id, to, subject));
            Db.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                Type = "Email",
                Payload = "{}",
                CreatedAtUtc = DateTime.UtcNow
            });
            return Task.FromResult(id);
        }

        public AppDbContext Db { get; set; } = default!;
    }
}
