using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentProjectionMonitoringIntegrationTests
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentProjectionMonitoringIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ProcessBatch_Should_Succeed_And_RecordMetrics_WithoutChangingOutcome()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();
        var metrics = scope.ServiceProvider.GetRequiredService<AppointmentProjectionMetrics>();

        using var listener = new MeterListener();
        long processedTotal = 0;
        listener.InstrumentPublished = (instrument, listenerRef) =>
        {
            if (instrument.Meter.Name == AppointmentProjectionMetrics.MeterName
                && instrument.Name == "appointment_projection.events.processed.total")
            {
                listenerRef.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => processedTotal += measurement);
        listener.Start();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 6, 16, 11, 0, 0, DateTimeKind.Utc));

        var integrationEvent = new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot);
        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, AppointmentIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(1);
        metrics.Should().NotBeNull();
        processedTotal.Should().Be(1);
    }

    [Fact]
    public void Startup_Should_FailFast_WhenMonitoringOptionsInvalid()
    {
        using var factory = new AppointmentProjectionInvalidMonitoringWebApplicationFactory();
        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>()
            .Which.Message.Should().Contain("CriticalPendingAgeSeconds");
    }
}

public sealed class AppointmentProjectionBrokenQueryHealthIntegrationTests
    : IClassFixture<AppointmentProjectionBrokenQueryReadEnabledWebApplicationFactory>,
      IClassFixture<AppointmentProjectionBrokenQueryReadDisabledWebApplicationFactory>
{
    private readonly AppointmentProjectionBrokenQueryReadEnabledWebApplicationFactory _readEnabledFactory;
    private readonly AppointmentProjectionBrokenQueryReadDisabledWebApplicationFactory _readDisabledFactory;

    public AppointmentProjectionBrokenQueryHealthIntegrationTests(
        AppointmentProjectionBrokenQueryReadEnabledWebApplicationFactory readEnabledFactory,
        AppointmentProjectionBrokenQueryReadDisabledWebApplicationFactory readDisabledFactory)
    {
        _readEnabledFactory = readEnabledFactory;
        _readDisabledFactory = readDisabledFactory;
    }

    [Fact]
    public async Task HealthReady_Should_BeUnhealthy_WhenQueryReadEnabledAndQueryDatabaseUnavailable()
    {
        var client = _readEnabledFactory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().NotContain("Password=");
        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("results").GetProperty("appointment-projection");
        entry.GetProperty("status").GetString().Should().Be("Unhealthy");
    }

    [Fact]
    public async Task HealthReady_Should_BeUnhealthy_WhenQueryReadDisabledAndQueryDatabaseUnavailable()
    {
        var client = _readDisabledFactory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("results").GetProperty("appointment-projection");
        entry.GetProperty("status").GetString().Should().Be("Unhealthy");
    }
}
