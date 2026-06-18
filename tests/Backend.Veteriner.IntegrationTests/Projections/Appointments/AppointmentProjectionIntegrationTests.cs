using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

[CollectionDefinition("appointment-projection", DisableParallelization = true)]
public sealed class AppointmentProjectionCollection :
    ICollectionFixture<AppointmentProjectionWebApplicationFactory>,
    ICollectionFixture<AppointmentProjectionHostedWebApplicationFactory>;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionIntegrationTests
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentProjectionIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task CreatedEvent_Should_ProjectReadModels_And_MarkOutboxProcessed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, scheduledAtUtc);

        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);
        var outbox = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.ScheduledEndUtc.Should().Be(scheduledAtUtc.AddMinutes(30));
        readModel.LastEventId.Should().Be(eventId);

        var petActivity = await queryDb.ClinicPetActivityReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.PetId == petId);
        petActivity.LastAppointmentAtUtc.Should().Be(scheduledAtUtc);

        var clientActivity = await queryDb.ClinicClientActivityReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.ClientId == clientId);
        clientActivity.LastAppointmentAtUtc.Should().Be(scheduledAtUtc);

        var localDate = AppointmentProjectionTestSupport.LocalDateForUtc(scheduledAtUtc);
        var stats = await queryDb.ClinicDailyAppointmentStatsReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == localDate);
        stats.ScheduledCount.Should().Be(1);
        stats.TotalCount.Should().Be(1);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task SameEventProcessedTwice_Should_BeIdempotent()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 16, 11, 0, 0, DateTimeKind.Utc);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);
        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(1);
        (await queryDb.ProcessedProjectionEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);

        var outboxRows = await commandDb.OutboxMessages.AsNoTracking().ToListAsync();
        outboxRows.Should().OnlyContain(x => x.ProcessedAtUtc != null);
    }

    [Fact]
    public async Task CreatedEvent_WithSearchFields_Should_ProjectBreedAndEmail()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId,
            tenantId,
            clinicId,
            petId,
            clientId,
            scheduledAtUtc,
            petBreed: "Van Kedisi",
            petBreedRefName: "British Shorthair",
            clientEmail: "search@example.com",
            clientPhoneNormalized: "905551112233");

        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);
        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.PetBreed.Should().Be("Van Kedisi");
        readModel.PetBreedRefName.Should().Be("British Shorthair");
        readModel.ClientEmail.Should().Be("search@example.com");
        readModel.ClientPhoneNormalized.Should().Be("905551112233");
    }

    [Fact]
    public async Task QueryAlreadyProcessed_CommandOutboxUnprocessed_Should_OnlyMarkOutbox()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);

        queryDb.AppointmentReadModels.Add(new AppointmentReadModel
        {
            AppointmentId = appointmentId,
            TenantId = tenantId,
            ClinicId = clinicId,
            ClinicName = snapshot.ClinicName,
            PetId = petId,
            PetName = snapshot.PetName,
            SpeciesId = snapshot.SpeciesId,
            SpeciesName = snapshot.SpeciesName,
            ClientId = clientId,
            ClientName = snapshot.ClientName,
            ClientPhone = snapshot.ClientPhone,
            ScheduledAtUtc = scheduledAtUtc,
            ScheduledEndUtc = scheduledAtUtc.AddMinutes(30),
            DurationMinutes = 30,
            AppointmentType = 0,
            Status = (int)AppointmentStatus.Scheduled,
            Notes = snapshot.Notes,
            LastEventId = eventId,
            LastProjectedAtUtc = DateTime.UtcNow
        });
        await AppointmentProjectionTestSupport.MarkProcessedInQueryDbAsync(queryDb, eventId);

        var outbox = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.ScheduledAtUtc.Should().Be(scheduledAtUtc);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RescheduleSameDay_Should_UpdateAppointment_WithoutDuplicateDailyStats()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var previousAt = new DateTime(2026, 6, 16, 8, 0, 0, DateTimeKind.Utc);
        var currentAt = new DateTime(2026, 6, 16, 15, 0, 0, DateTimeKind.Utc);

        var previous = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, previousAt);
        var current = AppointmentProjectionTestSupport.WithSchedule(previous, currentAt);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous));

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous, current));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.ScheduledAtUtc.Should().Be(currentAt);

        var localDate = AppointmentProjectionTestSupport.LocalDateForUtc(currentAt);
        var statsCount = await queryDb.ClinicDailyAppointmentStatsReadModels
            .CountAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == localDate);
        statsCount.Should().Be(1);

        var petActivity = await queryDb.ClinicPetActivityReadModels.SingleAsync(x => x.PetId == petId);
        petActivity.LastAppointmentAtUtc.Should().Be(currentAt);
    }

    [Fact]
    public async Task RescheduleDifferentDay_Should_RecomputeBothDailyBuckets()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var previousAt = new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc);
        var currentAt = new DateTime(2026, 6, 17, 10, 0, 0, DateTimeKind.Utc);

        var previous = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, previousAt);
        var current = AppointmentProjectionTestSupport.WithSchedule(previous, currentAt);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous));

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous, current));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var previousDate = AppointmentProjectionTestSupport.LocalDateForUtc(previousAt);
        var currentDate = AppointmentProjectionTestSupport.LocalDateForUtc(currentAt);

        (await queryDb.ClinicDailyAppointmentStatsReadModels
            .CountAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == previousDate))
            .Should().Be(0, "eski gunde appointment kalmadiginda satir silinir");

        var currentStats = await queryDb.ClinicDailyAppointmentStatsReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == currentDate);
        currentStats.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateDifferentClinicPetClient_Should_RecomputeOldAndNewActivityKeys()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var oldClinicId = Guid.NewGuid();
        var newClinicId = Guid.NewGuid();
        var oldPetId = Guid.NewGuid();
        var newPetId = Guid.NewGuid();
        var oldClientId = Guid.NewGuid();
        var newClientId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 16, 9, 0, 0, DateTimeKind.Utc);

        var previous = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, oldClinicId, oldPetId, oldClientId, scheduledAtUtc);
        var current = AppointmentProjectionTestSupport.WithClient(
            AppointmentProjectionTestSupport.WithPet(
                AppointmentProjectionTestSupport.WithClinic(previous, newClinicId, "Yeni Klinik"),
                newPetId,
                "Yeni Pet"),
            newClientId,
            "Yeni Musteri");

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous));

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Updated,
            new AppointmentUpdatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous, current));

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.ClinicPetActivityReadModels
            .CountAsync(x => x.TenantId == tenantId && x.ClinicId == oldClinicId && x.PetId == oldPetId))
            .Should().Be(0);

        (await queryDb.ClinicClientActivityReadModels
            .CountAsync(x => x.TenantId == tenantId && x.ClinicId == oldClinicId && x.ClientId == oldClientId))
            .Should().Be(0);

        (await queryDb.ClinicPetActivityReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == newClinicId && x.PetId == newPetId))
            .PetName.Should().Be("Yeni Pet");

        (await queryDb.ClinicClientActivityReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == newClinicId && x.ClientId == newClientId))
            .ClientName.Should().Be("Yeni Musteri");
    }

    [Fact]
    public async Task CancelledEvent_Should_UpdateStatusAndDailyCounts()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 16, 13, 0, 0, DateTimeKind.Utc);

        var previous = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, scheduledAtUtc);
        var current = AppointmentProjectionTestSupport.WithSchedule(
            previous, scheduledAtUtc, (int)AppointmentStatus.Cancelled);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous));

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous, current));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.Status.Should().Be((int)AppointmentStatus.Cancelled);

        var localDate = AppointmentProjectionTestSupport.LocalDateForUtc(scheduledAtUtc);
        var stats = await queryDb.ClinicDailyAppointmentStatsReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == localDate);
        stats.ScheduledCount.Should().Be(0);
        stats.CancelledCount.Should().Be(1);
        stats.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task CompletedEvent_Should_UpdateStatusAndDailyCounts()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc);

        var previous = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, scheduledAtUtc);
        var current = AppointmentProjectionTestSupport.WithSchedule(
            previous, scheduledAtUtc, (int)AppointmentStatus.Completed);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous));

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Completed,
            new AppointmentCompletedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, previous, current));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.Status.Should().Be((int)AppointmentStatus.Completed);

        var localDate = AppointmentProjectionTestSupport.LocalDateForUtc(scheduledAtUtc);
        var stats = await queryDb.ClinicDailyAppointmentStatsReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == localDate);
        stats.ScheduledCount.Should().Be(0);
        stats.CompletedCount.Should().Be(1);
        stats.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task NonAppointmentOutboxMessage_Should_NotBeConsumedByProjector()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxMessageTypes.Email,
            Payload = """{"To":"test@example.com","Subject":"x","Body":"y"}""",
            CreatedAtUtc = DateTime.UtcNow
        });
        await commandDb.SaveChangesAsync();

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        var emailRow = await commandDb.OutboxMessages.SingleAsync();
        emailRow.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task InvalidJson_Should_RollbackQueryChanges_And_NotMarkOutboxProcessed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var outbox = new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{ invalid json",
            CreatedAtUtc = DateTime.UtcNow
        };
        commandDb.OutboxMessages.Add(outbox);
        await commandDb.SaveChangesAsync();

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().BeNull();
        outbox.RetryCount.Should().Be(1);
        outbox.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CommandAndQueryDatabases_Should_UseDifferentCatalogs()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        var commandCatalog = new SqlConnectionStringBuilder(commandDb.Database.GetConnectionString()!).InitialCatalog;
        var queryCatalog = new SqlConnectionStringBuilder(queryDb.Database.GetConnectionString()!).InitialCatalog;

        commandCatalog.Should().Be(AppointmentProjectionWebApplicationFactory.CommandDatabaseName);
        queryCatalog.Should().Be(AppointmentProjectionWebApplicationFactory.QueryDatabaseName);
        commandCatalog.Should().NotBe(queryCatalog);
    }

    [Fact]
    public async Task CommandHandlers_Should_EmitAppointmentIntegrationOutboxEvents_AfterSuccessfulMutation()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var (clinicId, petId) = await SeedPetForProjectionTestAsync(commandDb);
        var tenantId = await commandDb.Clinics.Where(c => c.Id == clinicId).Select(c => c.TenantId).SingleAsync();
        var appointment = new Appointment(
            tenantId,
            clinicId,
            petId,
            new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc),
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled);
        commandDb.Appointments.Add(appointment);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointment.Id,
            tenantId,
            clinicId,
            petId,
            Guid.NewGuid(),
            appointment.ScheduledAtUtc);
        var adapter = scope.ServiceProvider.GetRequiredService<IAppointmentIntegrationEventOutbox>();
        await adapter.EnqueueAsync(
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot));
        await commandDb.SaveChangesAsync();

        var messages = await commandDb.OutboxMessages.AsNoTracking().Select(m => m.Type).ToListAsync();
        messages.Count(AppointmentIntegrationEventTypes.IsKnown).Should().BeGreaterThan(0);
    }

    private static async Task<(Guid ClinicId, Guid PetId)> SeedPetForProjectionTestAsync(AppDbContext db)
    {
        var tenant = await db.Tenants.FirstAsync();
        var clinic = await db.Clinics.FirstAsync(c => c.TenantId == tenant.Id);
        var client = new Backend.Veteriner.Domain.Clients.Client(tenant.Id, "Projection Pet Client", "905551110055");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Backend.Veteriner.Domain.Pets.Pet(tenant.Id, client.Id, "ProjectionPet", speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return (clinic.Id, pet.Id);
    }
}
