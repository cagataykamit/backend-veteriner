using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionStatusIntegrationTests : IClassFixture<AppointmentProjectionWebApplicationFactory>
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentProjectionStatusIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetStatus_Should_CountOnlyKnownAppointmentIntegrationEventTypes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow
        });
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = "email.send.v1",
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await commandDb.SaveChangesAsync();

        var status = await reader.GetStatusAsync(CancellationToken.None);

        status.PendingCount.Should().Be(1);
        status.DeadLetterCount.Should().Be(0);
        status.RetryWaitingCount.Should().Be(0);
        status.QueryDatabaseReachable.Should().BeTrue();
        status.ProjectionEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_Should_SplitPendingAndRetryWaiting()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Updated,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(10),
            RetryCount = 1
        });
        await commandDb.SaveChangesAsync();

        var status = await reader.GetStatusAsync(CancellationToken.None);

        status.PendingCount.Should().Be(1);
        status.RetryWaitingCount.Should().Be(1);
        status.NextRetryAtUtc.Should().NotBeNull();
        status.OldestPendingCreatedAtUtc.Should().NotBeNull();
        status.OldestPendingAge.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatus_Should_ReturnNullOldestAge_WhenNoPendingReadyMessages()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var status = await reader.GetStatusAsync(CancellationToken.None);

        status.PendingCount.Should().Be(0);
        status.OldestPendingCreatedAtUtc.Should().BeNull();
        status.OldestPendingAge.Should().BeNull();
    }
}
