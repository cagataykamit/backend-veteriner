using System.Net;
using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionHealthIntegrationTests : IClassFixture<AppointmentProjectionWebApplicationFactory>
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentProjectionHealthIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task HealthEndpoint_Should_ExposeAppointmentProjectionSafeDataFields()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("appointment-projection");
        json.Should().NotContain("ConnectionStrings");
        json.Should().NotContain("Password=");
        json.Should().NotContain("Payload");

        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("results").GetProperty("appointment-projection");
        entry.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        entry.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        entry.TryGetProperty("duration", out _).Should().BeTrue();

        var data = entry.GetProperty("data");
        data.GetProperty("pendingCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("retryWaitingCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("deadLetterCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("oldestPendingAgeSeconds").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("projectionEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("appointmentsReadEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("dashboardReadEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task HealthLiveEndpoint_Should_RemainLightweight()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Evaluate_Should_BeDegraded_WhenPendingAgeExceedsThreshold()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-15)
        });
        await commandDb.SaveChangesAsync();

        var statusReader = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();
        var status = await statusReader.GetStatusAsync(CancellationToken.None);
        var healthOptions = scope.ServiceProvider.GetRequiredService<IOptions<AppointmentProjectionHealthOptions>>().Value;
        var queryOptions = scope.ServiceProvider.GetRequiredService<IOptions<QueryReadModelsOptions>>().Value;

        var evaluation = AppointmentProjectionHealthEvaluator.Evaluate(status, healthOptions, queryOptions);
        evaluation.Level.Should().Be(AppointmentProjectionHealthLevel.Degraded);
    }

    [Fact]
    public async Task Evaluate_Should_BeUnhealthy_WhenDeadLetterExists()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            DeadLetterAtUtc = DateTime.UtcNow,
            RetryCount = 8
        });
        await commandDb.SaveChangesAsync();

        var statusReader = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();
        var status = await statusReader.GetStatusAsync(CancellationToken.None);
        var healthOptions = scope.ServiceProvider.GetRequiredService<IOptions<AppointmentProjectionHealthOptions>>().Value;
        var queryOptions = scope.ServiceProvider.GetRequiredService<IOptions<QueryReadModelsOptions>>().Value;

        var evaluation = AppointmentProjectionHealthEvaluator.Evaluate(status, healthOptions, queryOptions);
        evaluation.Level.Should().Be(AppointmentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public async Task Evaluate_Should_BeDegraded_WhenReadFlagsEnabledButProjectorDisabledWithoutPending()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var statusReader = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();
        var status = await statusReader.GetStatusAsync(CancellationToken.None);

        var evaluation = AppointmentProjectionHealthEvaluator.Evaluate(
            status,
            new AppointmentProjectionHealthOptions(),
            new QueryReadModelsOptions { AppointmentsEnabled = true, DashboardAppointmentsEnabled = false });

        evaluation.Level.Should().Be(AppointmentProjectionHealthLevel.Degraded);
        evaluation.Data["projectionEnabled"].Should().Be(false);
        evaluation.Data["appointmentsReadEnabled"].Should().Be(true);
    }
}
