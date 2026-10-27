using System.Net;
using System.Text.Json;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Clients;

[Collection("client-projection")]
public sealed class ClientProjectionHealthIntegrationTests
{
    private readonly ClientProjectionWebApplicationFactory _factory;

    public ClientProjectionHealthIntegrationTests(ClientProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task HealthEndpoint_Should_ExposeClientProjectionSafeDataFields()
    {
        await ResetBaselineAsync();

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("client-projection");
        json.Should().NotContain("ConnectionStrings");
        json.Should().NotContain("Password=");
        json.Should().NotContain("Payload");

        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("results").GetProperty("client-projection");
        entry.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        entry.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();

        var data = entry.GetProperty("data");
        data.GetProperty("pendingCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("retryWaitingCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("deadLetterCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("oldestPendingAgeSeconds").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("projectionEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("clientsReadEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task StatusReader_Should_ReportPendingClientEvent()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = ClientIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-5)
        });
        await commandDb.SaveChangesAsync();

        var statusReader = scope.ServiceProvider.GetRequiredService<IClientProjectionStatusReader>();
        var status = await statusReader.GetStatusAsync(CancellationToken.None);

        status.PendingCount.Should().BeGreaterThanOrEqualTo(1);
        status.OldestPendingAge.Should().NotBeNull();
    }

    [Fact]
    public async Task Evaluate_Should_BeDegraded_WhenPendingAgeExceedsDegradedThreshold()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = ClientIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-15)
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IClientProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        var evaluation = ClientProjectionHealthEvaluator.Evaluate(
            status,
            new ClientProjectionHealthOptions { DegradedAfterSeconds = 10, UnhealthyAfterSeconds = 30 },
            new QueryReadModelsOptions { ClientsEnabled = false });

        evaluation.Level.Should().Be(ClientProjectionHealthLevel.Degraded);
    }

    /// <summary>
    /// İçe aktarma Dilim T bulgusu: yoğun yazımda okuma anından (utcNow) hemen sonra yazılan olayın NextAttemptAtUtc
    /// değeri (yazım anı) utcNow'dan büyük olur. Hiç başarısız olmamış (RetryCount = 0) olay yeniden deneme bekleyen
    /// sayılmamalı; aksi halde sağlık sahte Degraded olur ve /health/ready 503 döner.
    /// </summary>
    [Fact]
    public async Task Evaluate_Should_StayHealthy_When_Fresh_Event_Is_Written_After_Status_Snapshot()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var writtenAt = DateTime.UtcNow.AddSeconds(5);
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = ClientIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = writtenAt,
            NextAttemptAtUtc = writtenAt,
            RetryCount = 0
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IClientProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        status.RetryWaitingCount.Should().Be(0);
        status.PendingCount.Should().Be(1);
        ClientProjectionHealthEvaluator.Evaluate(
                status,
                new ClientProjectionHealthOptions { DegradedAfterSeconds = 10, UnhealthyAfterSeconds = 30 },
                new QueryReadModelsOptions { ClientsEnabled = false })
            .Level.Should().Be(ClientProjectionHealthLevel.Healthy);
    }

    [Fact]
    public async Task Evaluate_Should_BeUnhealthy_WhenClientDeadLetterExists()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = ClientIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            DeadLetterAtUtc = DateTime.UtcNow,
            RetryCount = 8
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IClientProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        var evaluation = ClientProjectionHealthEvaluator.Evaluate(
            status,
            new ClientProjectionHealthOptions(),
            new QueryReadModelsOptions { ClientsEnabled = false });

        evaluation.Level.Should().Be(ClientProjectionHealthLevel.Unhealthy);
        evaluation.Data["deadLetterCount"].Should().Be(1);
    }

    [Fact]
    public async Task Evaluate_Should_BeDegraded_WhenReadFlagOnButProjectionDisabledWithoutPending()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var status = await scope.ServiceProvider
            .GetRequiredService<IClientProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        var evaluation = ClientProjectionHealthEvaluator.Evaluate(
            status,
            new ClientProjectionHealthOptions(),
            new QueryReadModelsOptions { ClientsEnabled = true });

        evaluation.Level.Should().Be(ClientProjectionHealthLevel.Degraded);
        evaluation.Data["projectionEnabled"].Should().Be(false);
        evaluation.Data["clientsReadEnabled"].Should().Be(true);
    }

    private async Task ResetBaselineAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);
        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }
}
