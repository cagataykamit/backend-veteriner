using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Reminders;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.IntegrationTests.Reminders;

/// <summary>
/// TIER-003 işleyicisi (docs/AUTOMATED_REMINDERS_API_CONTRACT.md §4): kontrol ve borç kuralı e-postası, tekrar engeli, hatırlatma tercihi,
/// paket kapısı, açınca birikmiş kayıtlara gitmeme; "dönmedi" kuralı onay ve merkezi anahtar olmadan gitmez.
/// </summary>
public sealed class AutomatedReminderRulesProcessorTests : IAsyncLifetime
{
    private const string CommandDatabasePrefix = "VetinityCommandDb_AutomatedReminders_";
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly List<string> _connectionStrings = [];
    private readonly RecordingEnqueuer _email = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            await IntegrationTestDatabaseReset.EnsureDroppedAsync(connectionString);
    }

    private sealed record Seeded(AppDbContext Db, Tenant Tenant, Clinic Clinic, Client Client, Pet Pet, ReminderProcessorService Service);

    private async Task<Seeded> SeedAsync(SubscriptionPlanCode plan = SubscriptionPlanCode.Pro, bool trialing = false, string? clientEmail = "ali@example.com")
    {
        var db = await CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var speciesId = await db.Species.AsNoTracking().OrderBy(x => x.DisplayOrder).Select(x => x.Id).FirstAsync();
        var tenant = new Tenant("Kural Kiracı", now);
        var clinic = new Clinic(tenant.Id, "Merkez", "İstanbul");
        var client = new Client(tenant.Id, "Ali Veli", now, "905555555555", clientEmail);
        var pet = new Pet(tenant.Id, client.Id, "Pamuk", speciesId);
        var subscription = TenantSubscription.StartTrial(tenant.Id, plan, now, 14);
        if (!trialing)
            subscription.ActivatePaidPlan(plan, now);
        db.AddRange(tenant, clinic, client, pet, subscription);
        await db.SaveChangesAsync();

        _email.Db = db;
        var service = new ReminderProcessorService(
            db,
            IntegrationTestDbContexts.TenantScopeOf(db),
            _email,
            TestAppointmentOwnerLinks.Issuer(),
            Options.Create(new ReminderProcessorOptions { Enabled = true, IntervalMinutes = 5, BatchSize = 100, WindowToleranceMinutes = 10 }),
            NullLogger<ReminderProcessorService>.Instance,
            TimeProvider.System);

        return new Seeded(db, tenant, clinic, client, pet, service);
    }

    private static async Task<ClinicReminderRule> EnableRuleAsync(Seeded s, ClinicReminderRuleType type, int days, DateTime enabledAtUtc)
    {
        var rule = ClinicReminderRule.CreateDisabled(s.Tenant.Id, s.Clinic.Id, type, enabledAtUtc);
        rule.Update(true, days, UserId, enabledAtUtc).IsSuccess.Should().BeTrue();
        s.Db.ClinicReminderRules.Add(rule);
        await s.Db.SaveChangesAsync();
        return rule;
    }

    private static async Task<Treatment> AddTreatmentAsync(Seeded s, DateTime followUpUtc)
    {
        var treatment = new Treatment(s.Tenant.Id, s.Clinic.Id, s.Pet.Id, null, DateTime.UtcNow.AddDays(-7), "Gizli tedavi", "Gizli açıklama", null, followUpUtc, DateTime.UtcNow);
        s.Db.Treatments.Add(treatment);
        await s.Db.SaveChangesAsync();
        return treatment;
    }

    private static async Task<Charge> AddChargeAsync(Seeded s, DateTime createdAtUtc, decimal unitPrice = 1500m)
    {
        var charge = Charge.Create(s.Tenant.Id, s.Clinic.Id, s.Client.Id, s.Pet.Id, null, null, "Muayene", 1m, unitPrice, UserId, createdAtUtc, null).Value!;
        s.Db.Charges.Add(charge);
        await s.Db.SaveChangesAsync();
        return charge;
    }

    [Fact]
    public async Task FollowUp_Should_Send_Once_Without_Clinical_Detail_And_Log()
    {
        var s = await SeedAsync();
        var treatment = await AddTreatmentAsync(s, DateTime.UtcNow.AddDays(2));
        await EnableRuleAsync(s, ClinicReminderRuleType.FollowUp, 3, DateTime.UtcNow);

        await s.Service.ProcessOnceAsync(CancellationToken.None);
        await s.Service.ProcessOnceAsync(CancellationToken.None);

        var sent = _email.Enqueued.Should().ContainSingle().Subject;
        sent.To.Should().Be("ali@example.com");
        sent.Body.Should().Contain("Pamuk").And.Contain("Merkez").And.Contain("almak istemiyorsanız");
        sent.Body.Should().NotContain("Gizli");
        var log = await s.Db.ReminderDispatchLogs.AsNoTracking().SingleAsync();
        log.ReminderType.Should().Be(ReminderType.FollowUp);
        log.SourceEntityType.Should().Be(ReminderSourceEntityType.Treatment);
        log.SourceEntityId.Should().Be(treatment.Id);
    }

    [Fact]
    public async Task FollowUp_Should_Not_Send_Outside_Window_Or_When_Rule_Off()
    {
        var s = await SeedAsync();
        await AddTreatmentAsync(s, DateTime.UtcNow.AddDays(10));
        await AddTreatmentAsync(s, DateTime.UtcNow.AddDays(-1));
        await EnableRuleAsync(s, ClinicReminderRuleType.FollowUp, 3, DateTime.UtcNow);

        await s.Service.ProcessOnceAsync(CancellationToken.None);
        _email.Enqueued.Should().BeEmpty();

        var near = await AddTreatmentAsync(s, DateTime.UtcNow.AddDays(1));
        var rule = await s.Db.ClinicReminderRules.SingleAsync();
        rule.Update(false, 3, UserId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await s.Db.SaveChangesAsync();

        await s.Service.ProcessOnceAsync(CancellationToken.None);
        _email.Enqueued.Should().BeEmpty();
        near.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FollowUp_Should_Respect_OptOut_And_Skip_Scheduled_Appointment()
    {
        var s = await SeedAsync();
        await AddTreatmentAsync(s, DateTime.UtcNow.AddDays(2));
        await EnableRuleAsync(s, ClinicReminderRuleType.FollowUp, 3, DateTime.UtcNow);

        s.Client.ApplyContactPreferences(null, remindersOptedOut: true).IsSuccess.Should().BeTrue();
        await s.Db.SaveChangesAsync();
        await s.Service.ProcessOnceAsync(CancellationToken.None);
        _email.Enqueued.Should().BeEmpty();
        (await s.Db.ReminderDispatchLogs.CountAsync()).Should().Be(0, "dışlanan kayıt günlüğe yazılmaz");

        s.Client.ApplyContactPreferences(null, remindersOptedOut: false).IsSuccess.Should().BeTrue();
        s.Db.Appointments.Add(new Appointment(s.Tenant.Id, s.Clinic.Id, s.Pet.Id, DateTime.UtcNow.AddDays(1), 30, AppointmentType.Checkup, AppointmentStatus.Scheduled));
        await s.Db.SaveChangesAsync();
        await s.Service.ProcessOnceAsync(CancellationToken.None);
        _email.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic, false, false)]
    [InlineData(SubscriptionPlanCode.Basic, true, false)]
    [InlineData(SubscriptionPlanCode.Pro, false, true)]
    [InlineData(SubscriptionPlanCode.Pro, true, true)]
    [InlineData(SubscriptionPlanCode.Premium, false, true)]
    public async Task Rules_Should_Run_Only_On_Plan_With_Feature(SubscriptionPlanCode plan, bool trialing, bool expectedSend)
    {
        var s = await SeedAsync(plan, trialing);
        await AddTreatmentAsync(s, DateTime.UtcNow.AddDays(2));
        await EnableRuleAsync(s, ClinicReminderRuleType.FollowUp, 3, DateTime.UtcNow);

        await s.Service.ProcessOnceAsync(CancellationToken.None);

        _email.Enqueued.Should().HaveCount(expectedSend ? 1 : 0);
    }

    [Fact]
    public async Task Email_Channel_Off_Should_Stop_Rules()
    {
        var s = await SeedAsync();
        await AddTreatmentAsync(s, DateTime.UtcNow.AddDays(2));
        await EnableRuleAsync(s, ClinicReminderRuleType.FollowUp, 3, DateTime.UtcNow);
        var settings = TenantReminderSettings.CreateDefault(s.Tenant.Id, DateTime.UtcNow);
        settings.Update(false, 24, false, 3, emailChannelEnabled: false, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        s.Db.TenantReminderSettings.Add(settings);
        await s.Db.SaveChangesAsync();

        await s.Service.ProcessOnceAsync(CancellationToken.None);

        _email.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Debt_Should_Send_Once_For_Oldest_Unpaid_Without_Amount_And_Not_Repeat()
    {
        var s = await SeedAsync();
        var oldest = await AddChargeAsync(s, DateTime.UtcNow.AddDays(-32));
        await AddChargeAsync(s, DateTime.UtcNow.AddDays(-31));
        await EnableRuleAsync(s, ClinicReminderRuleType.Debt, 30, DateTime.UtcNow.AddDays(-5));

        await s.Service.ProcessOnceAsync(CancellationToken.None);
        await s.Service.ProcessOnceAsync(CancellationToken.None);

        var sent = _email.Enqueued.Should().ContainSingle().Subject;
        sent.Subject.Should().Be("Ödeme hatırlatması");
        sent.Body.Should().NotContain("1500").And.NotContain("1.500");
        var log = await s.Db.ReminderDispatchLogs.AsNoTracking().SingleAsync();
        log.ReminderType.Should().Be(ReminderType.Debt);
        log.SourceEntityId.Should().Be(oldest.Id);
    }

    [Fact]
    public async Task Debt_Should_Not_Message_Backlog_When_Rule_Just_Enabled()
    {
        var s = await SeedAsync();
        await AddChargeAsync(s, DateTime.UtcNow.AddDays(-90));
        await EnableRuleAsync(s, ClinicReminderRuleType.Debt, 30, DateTime.UtcNow);

        await s.Service.ProcessOnceAsync(CancellationToken.None);

        _email.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Debt_Should_Skip_Client_With_Unallocated_Payment_And_Not_Yet_Due_Charge()
    {
        var s = await SeedAsync();
        await AddChargeAsync(s, DateTime.UtcNow.AddDays(-10));
        await EnableRuleAsync(s, ClinicReminderRuleType.Debt, 30, DateTime.UtcNow.AddDays(-5));
        await s.Service.ProcessOnceAsync(CancellationToken.None);
        _email.Enqueued.Should().BeEmpty("henüz N gün dolmadı");

        await AddChargeAsync(s, DateTime.UtcNow.AddDays(-32));
        s.Db.Payments.Add(new Payment(s.Tenant.Id, s.Clinic.Id, s.Client.Id, null, null, null, 500m, "TRY", PaymentMethod.Cash, DateTime.UtcNow.AddDays(-1), null));
        await s.Db.SaveChangesAsync();
        await s.Service.ProcessOnceAsync(CancellationToken.None);
        _email.Enqueued.Should().BeEmpty("bağlanmamış ödeme varken borç iletisi gitmez");
    }

    [Fact]
    public async Task Missing_Email_Should_Log_Skipped_And_Not_Send()
    {
        var s = await SeedAsync(clientEmail: null);
        await AddChargeAsync(s, DateTime.UtcNow.AddDays(-32));
        await EnableRuleAsync(s, ClinicReminderRuleType.Debt, 30, DateTime.UtcNow.AddDays(-5));

        await s.Service.ProcessOnceAsync(CancellationToken.None);

        _email.Enqueued.Should().BeEmpty();
        (await s.Db.ReminderDispatchLogs.AsNoTracking().SingleAsync()).Status.Should().Be(ReminderDispatchStatus.Skipped);
    }

    private static async Task AddVisitAsync(Seeded s, DateTime arrivedAtUtc)
    {
        s.Db.Visits.Add(new Visit(s.Tenant.Id, s.Clinic.Id, s.Pet.Id, null, null, UserId, arrivedAtUtc));
        await s.Db.SaveChangesAsync();
    }

    private static async Task AddMarketingConsentAsync(Seeded s, bool consented)
    {
        var account = OwnerAccount.Create("ali@example.com", "Ali", DateTime.UtcNow);
        var link = OwnerAccountClientLink.Create(account.Id, s.Tenant.Id, s.Clinic.Id, s.Client.Id, UserId, DateTime.UtcNow);
        if (consented)
            link.SetMarketingConsent(true, DateTime.UtcNow);
        s.Db.AddRange(account, link);
        await s.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task NoReturn_Should_Not_Send_While_Marketing_Sending_Is_Off_Even_With_Consent()
    {
        var s = await SeedAsync();
        await AddVisitAsync(s, DateTime.UtcNow.AddDays(-200));
        await AddMarketingConsentAsync(s, consented: true);
        await EnableRuleAsync(s, ClinicReminderRuleType.NoReturn, 180, DateTime.UtcNow.AddDays(-30));

        await s.Service.ProcessOnceAsync(CancellationToken.None);

        _email.Enqueued.Should().BeEmpty();
        (await s.Db.ReminderDispatchLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task NoReturn_Path_Should_Send_Once_Only_With_Consent_And_No_Later_Visit()
    {
        var s = await SeedAsync();
        await AddVisitAsync(s, DateTime.UtcNow.AddDays(-200));
        var rule = await EnableRuleAsync(s, ClinicReminderRuleType.NoReturn, 180, DateTime.UtcNow.AddDays(-30));

        await s.Service.ProcessNoReturnRuleAsync(rule, DateTime.UtcNow, 100, CancellationToken.None);
        _email.Enqueued.Should().BeEmpty("ticari ileti onayı yok");

        await AddMarketingConsentAsync(s, consented: true);
        await s.Service.ProcessNoReturnRuleAsync(rule, DateTime.UtcNow, 100, CancellationToken.None);
        await s.Service.ProcessNoReturnRuleAsync(rule, DateTime.UtcNow, 100, CancellationToken.None);
        _email.Enqueued.Should().ContainSingle();

        var other = new Pet(s.Tenant.Id, s.Client.Id, "Boncuk", s.Pet.SpeciesId);
        s.Db.Pets.Add(other);
        var firstVisit = new Visit(s.Tenant.Id, s.Clinic.Id, other.Id, null, null, UserId, DateTime.UtcNow.AddDays(-190));
        firstVisit.Start(DateTime.UtcNow.AddDays(-190)).IsSuccess.Should().BeTrue();
        firstVisit.Complete(DateTime.UtcNow.AddDays(-190)).IsSuccess.Should().BeTrue();
        s.Db.Visits.Add(firstVisit);
        s.Db.Visits.Add(new Visit(s.Tenant.Id, s.Clinic.Id, other.Id, null, null, UserId, DateTime.UtcNow.AddDays(-2)));
        await s.Db.SaveChangesAsync();
        await s.Service.ProcessNoReturnRuleAsync(rule, DateTime.UtcNow, 100, CancellationToken.None);
        _email.Enqueued.Should().ContainSingle("sonradan gelen hayvan dönmedi sayılmaz");
    }

    private async Task<AppDbContext> CreateDbContextAsync()
    {
        var commandDbName = IntegrationTestDatabaseNames.WithRunId($"{CommandDatabasePrefix}{Guid.NewGuid():N}");
        var connectionString = IntegrationTestDatabaseNames.ConnectionString(commandDbName);
        IntegrationTestDatabaseGuard.EnsureSafeDatabase(connectionString, allowedPrefix: CommandDatabasePrefix);
        _connectionStrings.Add(connectionString);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var db = IntegrationTestDbContexts.CreateSystem(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private sealed class RecordingEnqueuer : IReminderEmailOutboxEnqueuer
    {
        public List<(Guid Id, string To, string Subject, string Body)> Enqueued { get; } = [];

        public AppDbContext Db { get; set; } = default!;

        public Task<Guid> EnqueueReminderEmailAsync(string to, string subject, string body, bool isHtml, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            Enqueued.Add((id, to, subject, body));
            Db.OutboxMessages.Add(new OutboxMessage { Id = id, Type = "Email", Payload = "{}", CreatedAtUtc = DateTime.UtcNow });
            return Task.FromResult(id);
        }
    }
}
