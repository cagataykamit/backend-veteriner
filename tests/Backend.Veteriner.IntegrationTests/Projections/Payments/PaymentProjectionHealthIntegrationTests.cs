using System.Text.Json;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Payments;

[Collection("payment-projection")]
public sealed class PaymentProjectionHealthIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentProjectionHealthIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task HealthEndpoint_Should_ExposePaymentProjectionSafeDataFields()
    {
        await ResetBaselineAsync();

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("payment-projection");
        json.Should().NotContain("ConnectionStrings");
        json.Should().NotContain("Password=");

        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("results").GetProperty("payment-projection");
        entry.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();

        var data = entry.GetProperty("data");
        data.GetProperty("pendingCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("projectionEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Evaluate_Should_BeHealthy_WhenProjectionDisabled_EvenWithPendingPaymentEvents()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = PaymentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-20)
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IPaymentProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        var evaluation = PaymentProjectionHealthEvaluator.Evaluate(
            status,
            new PaymentProjectionHealthOptions { DegradedAfterSeconds = 10, UnhealthyAfterSeconds = 30 });

        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Healthy);
        evaluation.Data["pendingCount"].Should().Be(1);
        evaluation.Data["projectionEnabled"].Should().Be(false);
    }

    [Fact]
    public async Task Evaluate_Should_BeDegraded_WhenProjectionEnabledAndPendingAgeExceedsThreshold()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = PaymentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-15)
        });
        await commandDb.SaveChangesAsync();

        var status = new PaymentProjectionStatus(
            PendingCount: 1,
            RetryWaitingCount: 0,
            DeadLetterCount: 0,
            OldestPendingCreatedAtUtc: DateTime.UtcNow.AddSeconds(-15),
            OldestPendingAge: TimeSpan.FromSeconds(15),
            NextRetryAtUtc: null,
            QueryDatabaseReachable: true,
            QueryDatabaseHasPendingMigrations: false,
            ProjectionEnabled: true);

        var evaluation = PaymentProjectionHealthEvaluator.Evaluate(
            status,
            new PaymentProjectionHealthOptions { DegradedAfterSeconds = 10, UnhealthyAfterSeconds = 30 });

        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Degraded);
    }

    [Fact]
    public async Task Evaluate_Should_BeUnhealthy_WhenProjectionEnabledAndPendingAgeExceedsUnhealthyThreshold()
    {
        await ResetBaselineAsync();

        var status = new PaymentProjectionStatus(
            PendingCount: 1,
            RetryWaitingCount: 0,
            DeadLetterCount: 0,
            OldestPendingCreatedAtUtc: DateTime.UtcNow.AddSeconds(-45),
            OldestPendingAge: TimeSpan.FromSeconds(45),
            NextRetryAtUtc: null,
            QueryDatabaseReachable: true,
            QueryDatabaseHasPendingMigrations: false,
            ProjectionEnabled: true);

        var evaluation = PaymentProjectionHealthEvaluator.Evaluate(
            status,
            new PaymentProjectionHealthOptions { DegradedAfterSeconds = 10, UnhealthyAfterSeconds = 30 });

        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public async Task Evaluate_Should_BeUnhealthy_WhenProjectionEnabledAndDeadLetterExists()
    {
        await ResetBaselineAsync();

        var status = new PaymentProjectionStatus(
            PendingCount: 0,
            RetryWaitingCount: 0,
            DeadLetterCount: 1,
            OldestPendingCreatedAtUtc: null,
            OldestPendingAge: null,
            NextRetryAtUtc: null,
            QueryDatabaseReachable: true,
            QueryDatabaseHasPendingMigrations: false,
            ProjectionEnabled: true);

        var evaluation = PaymentProjectionHealthEvaluator.Evaluate(
            status,
            new PaymentProjectionHealthOptions { DeadLetterIsUnhealthy = true });

        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public async Task StatusReader_Should_ReportPendingPaymentEvent()
    {
        await ResetBaselineAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = PaymentIntegrationEventTypes.Updated,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-3)
        });
        await commandDb.SaveChangesAsync();

        var status = await scope.ServiceProvider
            .GetRequiredService<IPaymentProjectionStatusReader>()
            .GetStatusAsync(CancellationToken.None);

        status.PendingCount.Should().BeGreaterThanOrEqualTo(1);
        status.OldestPendingAge.Should().NotBeNull();
    }

    [Fact]
    public async Task ReadModelHealthReader_Should_ReportDrift_WhenNotBackfilled()
    {
        await ResetBaselineWithPaymentsAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenantId, clinicId, _) = await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicId, 100m, DateTime.UtcNow);
        await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicId, 200m, DateTime.UtcNow);

        var signal = await scope.ServiceProvider
            .GetRequiredService<IPaymentReadModelHealthReader>()
            .GetSignalAsync(CancellationToken.None);

        signal.CommandPaymentCount.Should().Be(2);
        signal.ReadModelCount.Should().Be(0);
        signal.CountInSync.Should().BeFalse();

        // Projection açık + drift + list read flag kapalı → Degraded (catch-up/backfill penceresi).
        var status = new PaymentProjectionStatus(
            PendingCount: 0,
            RetryWaitingCount: 0,
            DeadLetterCount: 0,
            OldestPendingCreatedAtUtc: null,
            OldestPendingAge: null,
            NextRetryAtUtc: null,
            QueryDatabaseReachable: true,
            QueryDatabaseHasPendingMigrations: false,
            ProjectionEnabled: true);

        var evaluation = PaymentProjectionHealthEvaluator.Evaluate(
            status,
            new PaymentProjectionHealthOptions(),
            signal);

        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Degraded);
    }

    [Fact]
    public async Task ReadModelHealthReader_Should_BeInSync_AfterBackfill()
    {
        await ResetBaselineWithPaymentsAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenantId, clinicId, _) = await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicId, 100m, DateTime.UtcNow);

        await scope.ServiceProvider
            .GetRequiredService<IPaymentReadModelBackfillService>()
            .BackfillAsync(tenantId, batchSize: 100, CancellationToken.None);

        var signal = await scope.ServiceProvider
            .GetRequiredService<IPaymentReadModelHealthReader>()
            .GetSignalAsync(CancellationToken.None);

        signal.CommandPaymentCount.Should().Be(1);
        signal.ReadModelCount.Should().Be(1);
        signal.CountInSync.Should().BeTrue();
    }

    private async Task ResetBaselineAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }

    private async Task ResetBaselineWithPaymentsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await commandDb.Payments.ExecuteDeleteAsync();
    }
}
