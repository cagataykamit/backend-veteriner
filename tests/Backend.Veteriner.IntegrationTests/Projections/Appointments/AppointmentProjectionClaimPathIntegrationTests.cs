using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionClaimPathIntegrationTests
{
    private readonly AppointmentProjectionClaimEnabledWebApplicationFactory _claimFactory;
    private readonly AppointmentProjectionClaimInstrumentedWebApplicationFactory _instrumentedFactory;

    public AppointmentProjectionClaimPathIntegrationTests(
        AppointmentProjectionClaimEnabledWebApplicationFactory claimFactory,
        AppointmentProjectionClaimInstrumentedWebApplicationFactory instrumentedFactory)
    {
        _claimFactory = claimFactory;
        _instrumentedFactory = instrumentedFactory;
    }

    [Fact]
    public async Task ClaimingEnabled_Should_UseClaimRepository_NotLegacyNullMetadataRows()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 21, 9, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        var legacy = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);
        var ordered = await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            integrationEvent,
            appointmentId,
            appointmentSequence: 1);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        await commandDb.Entry(legacy).ReloadAsync();
        await commandDb.Entry(ordered).ReloadAsync();

        legacy.ProcessedAtUtc.Should().BeNull();
        ordered.ProcessedAtUtc.Should().NotBeNull();
        ordered.ClaimToken.Should().BeNull();
    }

    [Fact]
    public async Task ClaimPath_SuccessfulApply_Should_MarkProcessedWithClaimToken()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        var outbox = await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            integrationEvent,
            appointmentId,
            appointmentSequence: 1);

        await processor.ProcessBatchAsync(CancellationToken.None);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
        outbox.ClaimedBy.Should().BeNull();
        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_StaleMarkProcessedFalse_Should_NotCountAsBatchFailure()
    {
        await using var scope = _instrumentedFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();
        var claimRepo = scope.ServiceProvider.GetRequiredService<TestAppointmentOutboxClaimRepository>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 21, 11, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            integrationEvent,
            appointmentId,
            appointmentSequence: 1);

        claimRepo.RejectNextMarkProcessed = true;

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(1);
        claimRepo.MarkProcessedCallCount.Should().Be(1);
        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_ApplyException_Should_MarkRetryWithClaimToken()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{ not-valid-json",
            AppointmentId = appointmentId,
            AppointmentSequence = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        await commandDb.SaveChangesAsync();

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        var outbox = await commandDb.OutboxMessages.AsNoTracking().SingleAsync();
        outbox.RetryCount.Should().Be(1);
        outbox.NextAttemptAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
        outbox.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ClaimPath_RetryThresholdExceeded_Should_MarkDeadLetterWithClaimToken()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{ not-valid-json",
            AppointmentId = appointmentId,
            AppointmentSequence = 1,
            RetryCount = 9,
            CreatedAtUtc = DateTime.UtcNow
        });
        await commandDb.SaveChangesAsync();

        await processor.ProcessBatchAsync(CancellationToken.None);

        var outbox = await commandDb.OutboxMessages.AsNoTracking().SingleAsync();
        outbox.DeadLetterAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
    }

    [Fact]
    public async Task ClaimPath_StaleRetryFalse_Should_NotThrow()
    {
        await using var scope = _instrumentedFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();
        var claimRepo = scope.ServiceProvider.GetRequiredService<TestAppointmentOutboxClaimRepository>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{ not-valid-json",
            AppointmentId = appointmentId,
            AppointmentSequence = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        await commandDb.SaveChangesAsync();

        claimRepo.RejectNextMarkRetry = true;

        var act = async () => await processor.ProcessBatchAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
        claimRepo.MarkRetryCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_MetadataMismatch_Should_DeadLetterWithoutRetryStorm()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            integrationEvent,
            appointmentId,
            appointmentSequence: 99);

        await processor.ProcessBatchAsync(CancellationToken.None);

        var outbox = await commandDb.OutboxMessages.AsNoTracking().SingleAsync();
        outbox.DeadLetterAtUtc.Should().NotBeNull();
        outbox.RetryCount.Should().Be(0);
        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ClaimPath_LiveLifecycle_Should_ProjectCreateRescheduleCancel()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
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
        var original = new DateTime(2026, 6, 21, 14, 0, 0, DateTimeKind.Utc);
        var rescheduled = new DateTime(2026, 6, 22, 14, 0, 0, DateTimeKind.Utc);

        var createdSnapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, original);
        var createdEvent = new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, 1L, createdSnapshot);
        await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, createdEvent, appointmentId, 1);

        var rescheduledSnapshot = AppointmentProjectionTestSupport.WithSchedule(createdSnapshot, rescheduled);
        var rescheduledEvent = new AppointmentRescheduledIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            2L,
            createdSnapshot,
            rescheduledSnapshot);
        await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Rescheduled, rescheduledEvent, appointmentId, 2);

        var cancelledSnapshot = AppointmentProjectionTestSupport.WithSchedule(
            rescheduledSnapshot,
            rescheduled,
            status: (int)AppointmentStatus.Cancelled);
        var cancelledEvent = new AppointmentCancelledIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            3L,
            rescheduledSnapshot,
            cancelledSnapshot);
        await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Cancelled, cancelledEvent, appointmentId, 3);

        (await processor.ProcessBatchAsync(CancellationToken.None)).Should().Be(1);
        (await processor.ProcessBatchAsync(CancellationToken.None)).Should().Be(1);
        (await processor.ProcessBatchAsync(CancellationToken.None)).Should().Be(1);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.Status.Should().Be((int)AppointmentStatus.Cancelled);
        readModel.ScheduledAtUtc.Should().Be(rescheduled);

        (await commandDb.OutboxMessages.CountAsync(m => m.ProcessedAtUtc == null)).Should().Be(0);
    }

    [Fact]
    public async Task ClaimPath_DuplicateEvent_Should_KeepReadModelAndStatsIdempotent()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 21, 15, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var firstEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);
        var duplicateDelivery = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 2L, snapshot);

        await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, firstEvent, appointmentId, 1);
        await AppointmentProjectionTestSupport.EnqueueOrderedIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, duplicateDelivery, appointmentId, 2);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(1);
        var localDate = AppointmentProjectionTestSupport.LocalDateForUtc(scheduledAtUtc);
        var stats = await queryDb.ClinicDailyAppointmentStatsReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == localDate);
        stats.TotalCount.Should().Be(1);
    }
}
