using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionHostedServiceIntegrationTests
{
    private readonly AppointmentProjectionHostedWebApplicationFactory _factory;

    public AppointmentProjectionHostedServiceIntegrationTests(AppointmentProjectionHostedWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task HostedService_Should_ProjectPendingOutbox_WithinBoundedTimeout()
    {
        await ResetAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        var appointmentId = Guid.NewGuid();
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(2));

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                1L,
                snapshot));

        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var pollScope = _factory.Services.CreateSystemScope();
                var polledCommandDb = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var polledQueryDb = pollScope.ServiceProvider.GetRequiredService<QueryDbContext>();
                var processed = await polledCommandDb.OutboxMessages
                    .AnyAsync(m => m.ProcessedAtUtc != null);
                var projected = await polledQueryDb.AppointmentReadModels
                    .AnyAsync(x => x.AppointmentId == appointmentId);
                return processed && projected;
            },
            timeout: TimeSpan.FromSeconds(15),
            pollInterval: TimeSpan.FromMilliseconds(300),
            because: "Hosted appointment projection should process pending outbox within bounded timeout.");
    }

    [Fact]
    public async Task HostedService_Should_ProcessCreateThenReschedule_InImmediateSuccession()
    {
        await ResetAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var createTime = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
        var rescheduleTime = createTime.AddMinutes(45);

        var createSnapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, tenantId, clinicId, petId, clientId, createTime);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, 1L, createSnapshot));

        var rescheduleSnapshot = AppointmentProjectionTestSupport.WithSchedule(
            createSnapshot, rescheduleTime, (int)AppointmentStatus.Scheduled);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                1L,
                createSnapshot,
                rescheduleSnapshot));

        var totalProcessed = 0;
        var iterations = 0;
        const int maxIterations = 10;

        while (totalProcessed < 2 && iterations < maxIterations)
        {
            var processedCount = await processor.ProcessBatchAsync(CancellationToken.None);
            totalProcessed += processedCount;
            iterations++;

            if (AppointmentProjectionPollingLoop.ShouldIdleWaitAfterBatch(processedCount))
                break;
        }

        // Barındırılan servis de aynı kuyruğu işler; mesajları ondan önce alabilir (CI'da elle sayaç 0 kaldı). Bu yüzden
        // kimin işlediğine değil sonuca bakılır: iki mesaj da işlenmiş ve okuma modeli yeniden planlanan saati taşıyor.
        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var pollScope = _factory.Services.CreateSystemScope();
                var polledCommandDb = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var polledQueryDb = pollScope.ServiceProvider.GetRequiredService<QueryDbContext>();
                var processed = await polledCommandDb.OutboxMessages.CountAsync(m => m.ProcessedAtUtc != null);
                var readModel = await polledQueryDb.AppointmentReadModels
                    .SingleOrDefaultAsync(x => x.AppointmentId == appointmentId);
                return processed == 2 && readModel?.ScheduledAtUtc == rescheduleTime;
            },
            timeout: TimeSpan.FromSeconds(15),
            pollInterval: TimeSpan.FromMilliseconds(300),
            because: "create and reschedule should both be projected (by the loop above or the hosted service).");
    }

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Appointments.ExecuteDeleteAsync();
        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }
}
