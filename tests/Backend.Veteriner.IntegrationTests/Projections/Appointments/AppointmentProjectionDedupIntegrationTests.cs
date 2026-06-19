using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionDedupIntegrationTests
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentProjectionDedupIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ProcessBatch_WhenDedupRowExists_Should_NotApplyReadModel()
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
        var scheduledAtUtc = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        await AppointmentProjectionTestSupport.MarkProcessedInQueryDbAsync(queryDb, eventId);
        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        (await queryDb.AppointmentReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ClinicDailyAppointmentStatsReadModels.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenDedupDuplicate_Should_NotProduceRetryOrDeadLetter()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        await AppointmentProjectionTestSupport.MarkProcessedInQueryDbAsync(queryDb, eventId);
        var outbox = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.DeadLetterAtUtc.Should().BeNull();
        outbox.RetryCount.Should().Be(0);
        outbox.NextAttemptAtUtc.Should().BeNull();
        outbox.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessBatch_WhenDedupInsertSucceeds_Should_ApplyReadModel()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(1);
        (await queryDb.ProcessedProjectionEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_DuplicateEventTwice_Should_NotDoubleDailyStats()
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
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);
        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        var localDate = AppointmentProjectionTestSupport.LocalDateForUtc(scheduledAtUtc);
        var stats = await queryDb.ClinicDailyAppointmentStatsReadModels
            .SingleAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.LocalDate == localDate);

        stats.TotalCount.Should().Be(1);
        stats.ScheduledCount.Should().Be(1);
    }

    [Fact]
    public async Task ClaimingDisabled_Should_KeepLegacyFifoPath()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var scheduledAtUtc = new DateTime(2026, 6, 20, 13, 0, 0, DateTimeKind.Utc);
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc);
        var integrationEvent = new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, 1L, snapshot);

        var outbox = await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
    }
}
