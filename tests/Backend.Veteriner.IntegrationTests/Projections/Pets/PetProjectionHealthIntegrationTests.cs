using System.Text.Json;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Pets;

[Collection("pet-projection")]
public sealed class PetProjectionHealthIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PetProjectionHealthIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task HealthEndpoint_Should_ExposePetProjectionSafeDataFields()
    {
        await ResetBaselineAsync();

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("pet-projection");
        json.Should().NotContain("ConnectionStrings");
        json.Should().NotContain("Password=");
        json.Should().NotContain("Payload");

        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("results").GetProperty("pet-projection");
        entry.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        entry.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();

        var data = entry.GetProperty("data");
        data.GetProperty("pendingCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("retryWaitingCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("deadLetterCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("oldestPendingAgeSeconds").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("projectionEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("petsReadEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task StatusReader_Should_ReportPendingPetEvent()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = PetIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-5)
        });
        await commandDb.SaveChangesAsync();

        var statusReader = scope.ServiceProvider.GetRequiredService<IPetProjectionStatusReader>();
        var status = await statusReader.GetStatusAsync(CancellationToken.None);

        status.PendingCount.Should().BeGreaterThanOrEqualTo(1);
        status.OldestPendingAge.Should().NotBeNull();
    }

    [Fact]
    public async Task StatusReader_Should_NotCountClientOrAppointmentEvents()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = "client.created.v1",
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-5)
        });
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = "appointment.created.v1",
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-5)
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IPetProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        status.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task Evaluate_Should_BeDegraded_WhenPendingAgeExceedsDegradedThreshold()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = PetIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-15)
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IPetProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        var evaluation = PetProjectionHealthEvaluator.Evaluate(
            status,
            new PetProjectionHealthOptions { DegradedAfterSeconds = 10, UnhealthyAfterSeconds = 30 },
            new QueryReadModelsOptions { PetsEnabled = false });

        evaluation.Level.Should().Be(PetProjectionHealthLevel.Degraded);
    }

    [Fact]
    public async Task Evaluate_Should_BeUnhealthy_WhenPetDeadLetterExists()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = PetIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            DeadLetterAtUtc = DateTime.UtcNow,
            RetryCount = 8
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IPetProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        var evaluation = PetProjectionHealthEvaluator.Evaluate(
            status,
            new PetProjectionHealthOptions(),
            new QueryReadModelsOptions { PetsEnabled = false });

        evaluation.Level.Should().Be(PetProjectionHealthLevel.Unhealthy);
        evaluation.Data["deadLetterCount"].Should().Be(1);
    }

    [Fact]
    public async Task Evaluate_Should_BeDegraded_WhenReadFlagOnButProjectionDisabledWithoutPending()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var status = await scope.ServiceProvider
            .GetRequiredService<IPetProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        var evaluation = PetProjectionHealthEvaluator.Evaluate(
            status,
            new PetProjectionHealthOptions(),
            new QueryReadModelsOptions { PetsEnabled = true });

        evaluation.Level.Should().Be(PetProjectionHealthLevel.Degraded);
        evaluation.Data["projectionEnabled"].Should().Be(false);
        evaluation.Data["petsReadEnabled"].Should().Be(true);
    }

    private async Task ResetBaselineAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }
}
