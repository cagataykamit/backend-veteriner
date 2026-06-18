using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionRebuildIntegrationTests
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentProjectionRebuildIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Rebuild_WithEmptyCommandDb_Should_ClearQueryTables()
    {
        await ResetCommandAppointmentsAsync();

        var result = await RunRebuildAsync();

        result.Success.Should().BeTrue();
        result.CommandAppointmentCount.Should().Be(0);
        result.QueryAppointmentCount.Should().Be(0);
        result.PetActivityCount.Should().Be(0);
        result.ClientActivityCount.Should().Be(0);
        result.DailyStatsCount.Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ClinicPetActivityReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ClinicClientActivityReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ClinicDailyAppointmentStatsReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rebuild_WithMixedData_Should_ProduceParity()
    {
        await ResetCommandAppointmentsAsync();
        var seedCount = await SeedMixedAppointmentsAsync();

        var result = await RunRebuildAsync();

        result.Success.Should().BeTrue();
        result.CommandAppointmentCount.Should().Be(seedCount);
        result.QueryAppointmentCount.Should().Be(seedCount);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var rows = await queryDb.AppointmentReadModels.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(seedCount);
        rows.Select(x => x.AppointmentId).Should().OnlyHaveUniqueItems();
        rows.Should().AllSatisfy(x =>
        {
            x.TenantId.Should().NotBe(Guid.Empty);
            x.ClinicId.Should().NotBe(Guid.Empty);
            x.PetId.Should().NotBe(Guid.Empty);
            x.ClientId.Should().NotBe(Guid.Empty);
            x.ScheduledEndUtc.Should().BeOnOrAfter(x.ScheduledAtUtc);
            x.LastEventId.Should().Be(Guid.Empty);
        });
    }

    [Fact]
    public async Task Rebuild_Should_PopulateSearchParityFields()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = await db.Clients.SingleAsync(c => c.Id == context.ClientId);
            client.UpdateDetails(client.FullName, "rebuild.search@example.com", client.Phone, client.Address);
            var pet = await db.Pets.SingleAsync(p => p.Id == context.PetId);
            pet.UpdateDetails(pet.Name, pet.SpeciesId, "RebuildBreedFreeText", pet.BirthDate, pet.BreedId, pet.Gender, pet.ColorId, pet.Weight, pet.Notes);
            await db.SaveChangesAsync();
        }

        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await RunRebuildAsync();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var queryDb = verifyScope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var row = await queryDb.AppointmentReadModels.AsNoTracking().SingleAsync();
        row.PetBreed.Should().Be("RebuildBreedFreeText");
        row.ClientEmail.Should().Be("rebuild.search@example.com");
        row.ClientPhoneNormalized.Should().NotBeNull();
    }

    [Fact]
    public async Task Rebuild_Should_BucketDailyStatsByIstanbulLocalDate()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();

        var dayOneUtc = new DateTime(2026, 3, 15, 20, 30, 0, DateTimeKind.Utc);
        var dayTwoUtc = new DateTime(2026, 3, 15, 21, 30, 0, DateTimeKind.Utc);
        OperationDayBounds.ToLocalDate(dayOneUtc).Should().NotBe(OperationDayBounds.ToLocalDate(dayTwoUtc));

        await SeedAppointmentAsync(context, context.PetId, dayOneUtc, AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(context, context.PetId, dayTwoUtc, AppointmentStatus.Completed);

        await RunRebuildAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var stats = await queryDb.ClinicDailyAppointmentStatsReadModels.AsNoTracking().ToListAsync();
        stats.Should().HaveCount(2);
        foreach (var stat in stats)
            (stat.ScheduledCount + stat.CompletedCount + stat.CancelledCount).Should().Be(stat.TotalCount);
    }

    [Fact]
    public async Task Rebuild_WithSameScheduledAtUtc_Should_KeepSinglePetActivityRow()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();
        var when = SlotAlignedUtcPlusDays(2);

        var firstId = await SeedAppointmentAsync(context, context.PetId, when, AppointmentStatus.Scheduled);
        var secondId = await SeedAppointmentAsync(context, context.PetId, when, AppointmentStatus.Completed);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await commandDb.Appointments
                .Where(a => a.Id == firstId || a.Id == secondId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.ScheduledAtUtc, when));
        }

        var result = await RunRebuildAsync();

        result.PetActivityCount.Should().Be(1);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var queryDb = verifyScope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(2);
        var activity = await queryDb.ClinicPetActivityReadModels.AsNoTracking().SingleAsync();
        activity.LastAppointmentAtUtc.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));

        var winnerId = new[] { firstId, secondId }.OrderBy(x => x).First();
        var winner = await queryDb.AppointmentReadModels.AsNoTracking().SingleAsync(x => x.AppointmentId == winnerId);
        activity.PetName.Should().Be(winner.PetName);
    }

    [Fact]
    public async Task Rebuild_Should_SelectLatestClientActivity()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();

        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled, "older");
        var newerPet = await SeedExtraPetAsync(context, "NewerPet");
        await SeedAppointmentAsync(context, newerPet, SlotAlignedUtcPlusDays(5), AppointmentStatus.Completed, "newer");

        await RunRebuildAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var activity = await queryDb.ClinicClientActivityReadModels.AsNoTracking().SingleAsync();
        activity.ClientName.Should().Contain(context.ClientName);
        activity.LastAppointmentAtUtc.Should().BeCloseTo(SlotAlignedUtcPlusDays(5), TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Rebuild_Should_RemoveStaleQueryRows()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();
        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            queryDb.AppointmentReadModels.Add(new AppointmentReadModel
            {
                AppointmentId = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                ClinicId = Guid.NewGuid(),
                ClinicName = "Stale",
                PetId = Guid.NewGuid(),
                PetName = "Stale",
                SpeciesId = Guid.NewGuid(),
                SpeciesName = "Stale",
                ClientId = Guid.NewGuid(),
                ClientName = "Stale",
                ScheduledAtUtc = DateTime.UtcNow,
                ScheduledEndUtc = DateTime.UtcNow.AddMinutes(30),
                DurationMinutes = 30,
                AppointmentType = 0,
                Status = (int)AppointmentStatus.Scheduled,
                LastEventId = Guid.NewGuid(),
                LastProjectedAtUtc = DateTime.UtcNow
            });
            await queryDb.SaveChangesAsync();
        }

        var result = await RunRebuildAsync();
        result.QueryAppointmentCount.Should().Be(1);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await verifyDb.AppointmentReadModels.CountAsync()).Should().Be(1);
        (await verifyDb.AppointmentReadModels.AnyAsync(x => x.ClinicName == "Stale")).Should().BeFalse();
    }

    [Fact]
    public async Task Rebuild_RunTwice_Should_ProduceSameCounts()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();
        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(4), AppointmentStatus.Cancelled);

        var first = await RunRebuildAsync();
        var second = await RunRebuildAsync();

        second.CommandAppointmentCount.Should().Be(first.CommandAppointmentCount);
        second.QueryAppointmentCount.Should().Be(first.QueryAppointmentCount);
        second.PetActivityCount.Should().Be(first.PetActivityCount);
        second.ClientActivityCount.Should().Be(first.ClientActivityCount);
        second.DailyStatsCount.Should().Be(first.DailyStatsCount);
    }

    [Fact]
    public async Task Rebuild_WithPendingAppointmentOutbox_Should_Reject()
    {
        await ResetCommandAppointmentsAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var context = await GetDefaultTenantContextAsync();
        var appointmentId = await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                AppointmentProjectionTestSupport.CreateSnapshot(
                    appointmentId,
                    context.TenantId,
                    context.ClinicId,
                    context.PetId,
                    context.ClientId,
                    SlotAlignedUtcPlusDays(2))));

        var act = () => RunRebuildAsync();
        var ex = await act.Should().ThrowAsync<AppointmentProjectionRebuildException>();
        ex.Which.PendingAppointmentOutboxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Rebuild_WithDeadLetterAppointmentOutbox_Should_Reject()
    {
        await ResetCommandAppointmentsAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            DeadLetterAtUtc = DateTime.UtcNow
        });
        await commandDb.SaveChangesAsync();

        var act = () => RunRebuildAsync();
        var ex = await act.Should().ThrowAsync<AppointmentProjectionRebuildException>();
        ex.Which.DeadLetterAppointmentOutboxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Rebuild_WhenFailsMidway_Should_RollbackQueryChanges()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();
        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(4), AppointmentStatus.Completed);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            await queryDb.AppointmentReadModels.ExecuteDeleteAsync();
            queryDb.AppointmentReadModels.Add(new AppointmentReadModel
            {
                AppointmentId = Guid.NewGuid(),
                TenantId = context.TenantId,
                ClinicId = context.ClinicId,
                ClinicName = "Preserved",
                PetId = context.PetId,
                PetName = "Preserved",
                SpeciesId = context.SpeciesId,
                SpeciesName = "Preserved",
                ClientId = context.ClientId,
                ClientName = "Preserved",
                ScheduledAtUtc = DateTime.UtcNow,
                ScheduledEndUtc = DateTime.UtcNow.AddMinutes(30),
                DurationMinutes = 30,
                AppointmentType = 0,
                Status = (int)AppointmentStatus.Scheduled,
                LastEventId = Guid.NewGuid(),
                LastProjectedAtUtc = DateTime.UtcNow
            });
            await queryDb.SaveChangesAsync();
        }

        await using var failScope = _factory.Services.CreateAsyncScope();
        var commandDb = failScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDbForFail = failScope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var logger = failScope.ServiceProvider.GetRequiredService<ILogger<AppointmentProjectionRebuildService>>();
        var failing = new FailingAfterFirstBatchRebuildService(commandDb, queryDbForFail, logger);

        var act = () => failing.RebuildAsync(batchSize: 1, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await verifyDb.AppointmentReadModels.CountAsync()).Should().Be(1);
        (await verifyDb.AppointmentReadModels.AnyAsync(x => x.ClinicName == "Preserved")).Should().BeTrue();
        (await verifyDb.AppointmentReadModels.AnyAsync(x => x.ClinicName != "Preserved")).Should().BeFalse();
    }

    [Fact]
    public async Task Rebuild_Should_NotModifyCommandDbRows()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();
        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await commandDb.Appointments.AsNoTracking()
            .Select(a => new { a.Id, a.Status, a.ScheduledAtUtc, a.Notes })
            .ToListAsync();

        await RunRebuildAsync();

        var after = await commandDb.Appointments.AsNoTracking()
            .Select(a => new { a.Id, a.Status, a.ScheduledAtUtc, a.Notes })
            .ToListAsync();

        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Rebuild_Should_LeaveProcessedProjectionEventsEmpty()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();
        await SeedAppointmentAsync(context, context.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);

        await RunRebuildAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void CommandAndQueryDatabases_Should_BeDistinct()
    {
        var commandCatalog = new SqlConnectionStringBuilder(AppointmentProjectionWebApplicationFactory.CommandConnection).InitialCatalog;
        var queryCatalog = new SqlConnectionStringBuilder(AppointmentProjectionWebApplicationFactory.QueryConnection).InitialCatalog;
        commandCatalog.Should().NotBe(queryCatalog);
    }

    [Fact]
    public async Task Rebuild_WithSeveralThousandAppointments_Should_ProcessInBatches()
    {
        await ResetCommandAppointmentsAsync();
        var context = await GetDefaultTenantContextAsync();
        const int total = 2500;
        const int batchSize = 500;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < total; i++)
            {
                var when = SlotAlignedUtcPlusDays(2 + (i % 30));
                commandDb.Appointments.Add(new Appointment(
                    context.TenantId,
                    context.ClinicId,
                    context.PetId,
                    when,
                    30,
                    AppointmentType.Consultation,
                    (i % 3) switch
                    {
                        0 => AppointmentStatus.Scheduled,
                        1 => AppointmentStatus.Completed,
                        _ => AppointmentStatus.Cancelled
                    }));
            }

            await commandDb.SaveChangesAsync();
        }

        var result = await RunRebuildAsync(batchSize);
        result.CommandAppointmentCount.Should().Be(total);
        result.QueryAppointmentCount.Should().Be(total);
        result.DailyStatsCount.Should().BeGreaterThan(0);
    }

    private async Task<AppointmentProjectionRebuildResult> RunRebuildAsync(int batchSize = 1000)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionRebuildService>();
        return await rebuild.RebuildAsync(batchSize, CancellationToken.None);
    }

    private async Task ResetCommandAppointmentsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Appointments.ExecuteDeleteAsync();
    }

    private async Task<int> SeedMixedAppointmentsAsync()
    {
        var defaultContext = await GetDefaultTenantContextAsync();
        var secondTenant = await SeedSecondTenantAsync();

        await SeedAppointmentAsync(defaultContext, defaultContext.PetId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(defaultContext, defaultContext.PetId, SlotAlignedUtcPlusDays(4), AppointmentStatus.Completed);
        await SeedAppointmentAsync(defaultContext, defaultContext.PetId, SlotAlignedUtcPlusDays(6), AppointmentStatus.Cancelled);
        await SeedAppointmentAsync(secondTenant, secondTenant.PetId, SlotAlignedUtcPlusDays(3), AppointmentStatus.Scheduled);

        return 4;
    }

    private async Task<SeedContext> GetDefaultTenantContextAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();

        var client = await db.Clients.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
        if (client is null)
        {
            client = new Client(tenant.Id, "Rebuild Default Client", "905551110088");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        }

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.TenantId == tenant.Id && p.ClientId == client.Id);
        if (pet is null)
        {
            pet = new Pet(tenant.Id, client.Id, "RebuildDefaultPet", speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
        }

        return new SeedContext(
            tenant.Id,
            clinic.Id,
            pet.Id,
            client.Id,
            speciesId,
            client.FullName);
    }

    private async Task<SeedContext> SeedSecondTenantAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant($"RebuildTenant-{Guid.NewGuid():N}"[..20]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var clinic = new Clinic(tenant.Id, $"RebuildClinic-{Guid.NewGuid():N}"[..16], "Ankara");
        db.Clinics.Add(clinic);
        var client = new Client(tenant.Id, "Rebuild Client", "905551110077");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, "RebuildPet", speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        return new SeedContext(tenant.Id, clinic.Id, pet.Id, client.Id, speciesId, client.FullName);
    }

    private async Task<Guid> SeedExtraPetAsync(SeedContext context, string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pet = new Pet(context.TenantId, context.ClientId, name, context.SpeciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return pet.Id;
    }

    private async Task<Guid> SeedAppointmentAsync(
        SeedContext context,
        Guid petId,
        DateTime when,
        AppointmentStatus status,
        string? notes = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = new Appointment(
            context.TenantId,
            context.ClinicId,
            petId,
            when,
            30,
            AppointmentType.Consultation,
            status,
            notes);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            date = date.AddDays(1);
        return date.AddHours(9);
    }

    private sealed record SeedContext(
        Guid TenantId,
        Guid ClinicId,
        Guid PetId,
        Guid ClientId,
        Guid SpeciesId,
        string ClientName);

    private sealed class FailingAfterFirstBatchRebuildService : AppointmentProjectionRebuildService
    {
        public FailingAfterFirstBatchRebuildService(
            AppDbContext commandDb,
            QueryDbContext queryDb,
            ILogger<AppointmentProjectionRebuildService> logger)
            : base(commandDb, queryDb, logger)
        {
        }

        protected override Task OnAfterBatchSavedAsync(int batchIndex, CancellationToken cancellationToken)
        {
            if (batchIndex == 0)
                throw new InvalidOperationException("Test-induced rebuild failure.");
            return base.OnAfterBatchSavedAsync(batchIndex, cancellationToken);
        }
    }
}
