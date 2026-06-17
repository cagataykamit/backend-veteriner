using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection-hosted")]
public sealed class AppointmentProjectionHostedServiceIntegrationTests : IClassFixture<AppointmentProjectionHostedWebApplicationFactory>
{
    private readonly AppointmentProjectionHostedWebApplicationFactory _factory;

    public AppointmentProjectionHostedServiceIntegrationTests(AppointmentProjectionHostedWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task HostedService_Should_ProjectPendingOutbox_WithinBoundedTimeout()
    {
        await ResetAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
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
            Backend.Veteriner.Application.Appointments.IntegrationEvents.AppointmentIntegrationEventTypes.Created,
            new Backend.Veteriner.Application.Appointments.IntegrationEvents.AppointmentCreatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                snapshot));

        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var pollScope = _factory.Services.CreateAsyncScope();
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

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Appointments.ExecuteDeleteAsync();
        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }
}
