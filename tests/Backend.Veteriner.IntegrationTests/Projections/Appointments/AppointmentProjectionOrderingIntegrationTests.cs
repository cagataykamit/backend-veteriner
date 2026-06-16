using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionOrderingIntegrationTests : IClassFixture<AppointmentProjectionWebApplicationFactory>
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentProjectionOrderingIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ProcessBatch_Should_NotProcessNewerEvent_WhenOldestEventIsWaitingForRetry()
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
        var baseTime = new DateTime(2026, 6, 16, 8, 0, 0, DateTimeKind.Utc);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, baseTime);

        var older = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot),
            CancellationToken.None);

        var newerCancelled = AppointmentProjectionTestSupport.WithSchedule(
            snapshot, baseTime, (int)AppointmentStatus.Cancelled);

        var newer = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                snapshot,
                newerCancelled),
            CancellationToken.None);

        older.NextAttemptAtUtc = DateTime.UtcNow.AddHours(1);
        older.RetryCount = 1;
        await commandDb.SaveChangesAsync();

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(newer).ReloadAsync();
        newer.ProcessedAtUtc.Should().BeNull();

        var readModel = await queryDb.AppointmentReadModels.FirstOrDefaultAsync(x => x.AppointmentId == appointmentId);
        readModel.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatch_Should_StopSameBatch_WhenOldestEventFails()
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
        var baseTime = new DateTime(2026, 6, 16, 9, 0, 0, DateTimeKind.Utc);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, baseTime);

        var invalidOlder = new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{ invalid json",
            CreatedAtUtc = baseTime.AddMinutes(-10)
        };
        commandDb.OutboxMessages.Add(invalidOlder);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                snapshot,
                AppointmentProjectionTestSupport.WithSchedule(snapshot, baseTime, (int)AppointmentStatus.Cancelled)),
            CancellationToken.None);

        await commandDb.SaveChangesAsync();

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(invalidOlder).ReloadAsync();
        invalidOlder.RetryCount.Should().Be(1);

        var newerRows = await commandDb.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == AppointmentIntegrationEventTypes.Cancelled)
            .ToListAsync();
        newerRows.Should().ContainSingle();
        newerRows[0].ProcessedAtUtc.Should().BeNull();
        newerRows[0].RetryCount.Should().Be(0);

        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatch_Should_AdvanceAfterOldestEventIsDeadLettered()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();
        var outboxOptions = scope.ServiceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, baseTime);

        var deadLetter = new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{ invalid json",
            CreatedAtUtc = baseTime.AddMinutes(-20),
            RetryCount = outboxOptions.MaxRetryCount,
            DeadLetterAtUtc = DateTime.UtcNow,
            LastError = "forced dead-letter"
        };
        commandDb.OutboxMessages.Add(deadLetter);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot),
            CancellationToken.None);

        await commandDb.SaveChangesAsync();

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.Status.Should().Be((int)AppointmentStatus.Scheduled);

        await commandDb.Entry(deadLetter).ReloadAsync();
        deadLetter.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatch_Should_MarkIdempotentHeadProcessed_AndContinueToNextEvent()
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
        var baseTime = new DateTime(2026, 6, 16, 11, 0, 0, DateTimeKind.Utc);
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, baseTime);

        await AppointmentProjectionTestSupport.MarkProcessedInQueryDbAsync(queryDb, firstEventId);

        var firstOutbox = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(firstEventId, DateTime.UtcNow, snapshot),
            CancellationToken.None);

        var cancelledSnapshot = AppointmentProjectionTestSupport.WithSchedule(
            snapshot, baseTime, (int)AppointmentStatus.Cancelled);

        var secondOutbox = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(secondEventId, DateTime.UtcNow, snapshot, cancelledSnapshot),
            CancellationToken.None);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(2);

        await commandDb.Entry(firstOutbox).ReloadAsync();
        await commandDb.Entry(secondOutbox).ReloadAsync();
        firstOutbox.ProcessedAtUtc.Should().NotBeNull();
        secondOutbox.ProcessedAtUtc.Should().NotBeNull();

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.Status.Should().Be((int)AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task ProcessBatch_Should_PreserveCreatedRescheduledCancelledOrder()
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
        var createdAt = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);
        var rescheduledAt = new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc);

        var createdSnapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, createdAt);
        var rescheduledSnapshot = AppointmentProjectionTestSupport.WithSchedule(createdSnapshot, rescheduledAt);
        var cancelledSnapshot = AppointmentProjectionTestSupport.WithSchedule(
            rescheduledSnapshot, rescheduledAt, (int)AppointmentStatus.Cancelled);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, createdSnapshot),
            CancellationToken.None);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, createdSnapshot, rescheduledSnapshot),
            CancellationToken.None);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, rescheduledSnapshot, cancelledSnapshot),
            CancellationToken.None);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(3);

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.ScheduledAtUtc.Should().Be(rescheduledAt);
        readModel.Status.Should().Be((int)AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task ProcessBatch_Should_BlockLaterEvents_WhenMiddleEventIsWaitingForRetry()
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
        var createdAt = new DateTime(2026, 6, 16, 13, 0, 0, DateTimeKind.Utc);
        var rescheduledAt = new DateTime(2026, 6, 16, 15, 0, 0, DateTimeKind.Utc);

        var createdSnapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, createdAt);
        var rescheduledSnapshot = AppointmentProjectionTestSupport.WithSchedule(createdSnapshot, rescheduledAt);
        var cancelledSnapshot = AppointmentProjectionTestSupport.WithSchedule(
            rescheduledSnapshot, rescheduledAt, (int)AppointmentStatus.Cancelled);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, createdSnapshot),
            CancellationToken.None);

        var staleReschedule = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, createdSnapshot, rescheduledSnapshot),
            CancellationToken.None);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, rescheduledSnapshot, cancelledSnapshot),
            CancellationToken.None);

        staleReschedule.NextAttemptAtUtc = DateTime.UtcNow.AddHours(2);
        staleReschedule.RetryCount = 1;
        await commandDb.SaveChangesAsync();

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1, "cancel, reschedule retry beklerken islenmemeli");

        var readModel = await queryDb.AppointmentReadModels.SingleAsync(x => x.AppointmentId == appointmentId);
        readModel.Status.Should().Be((int)AppointmentStatus.Scheduled);
        readModel.ScheduledAtUtc.Should().Be(createdAt);
    }
}
