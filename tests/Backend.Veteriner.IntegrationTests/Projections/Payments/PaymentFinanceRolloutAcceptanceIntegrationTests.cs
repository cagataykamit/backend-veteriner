using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Payments;

[Collection("payment-projection")]
public sealed class PaymentFinanceRolloutAcceptanceIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentFinanceRolloutAcceptanceIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ProjectionDisabled_Should_NotInvokeHostedProcessor()
    {
        var probe = new ProbeProcessor();
        var services = new ServiceCollection();
        services.AddScoped<IPaymentProjectionProcessor>(_ => probe);
        var sp = services.BuildServiceProvider();

        var hosted = new PaymentProjectionHostedService(
            sp,
            Options.Create(new PaymentProjectionOptions { Enabled = false }),
            NullLogger<PaymentProjectionHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(250);
        await hosted.StopAsync(CancellationToken.None);

        probe.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task ProjectionEnabled_Should_ProcessPendingEvent_AfterBackfillWithoutDoubleCount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentFinanceBackfillService>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Payments.ExecuteDeleteAsync();

        var (tenantId, clinicId, _) = await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        var paidAt = new DateTime(2026, 6, 24, 16, 0, 0, DateTimeKind.Utc);
        var seed = await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicId, 400m, paidAt);

        await backfill.BackfillAsync(tenantId, cancellationToken: CancellationToken.None);

        var eventId = Guid.NewGuid();
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(
            seed.PaymentId, tenantId, clinicId, amount: 400m, paidAtUtc: paidAt);
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, seed.LocalDate, "TRY");
        stats!.PaidTotalAmount.Should().Be(400m);
        stats.PaidCount.Should().Be(1);

        (await queryDb.ProcessedProjectionEvents.CountAsync(
            x => x.EventId == eventId && x.ConsumerName == PaymentProjectionTestSupport.ConsumerName))
            .Should().Be(1);

        await processor.ProcessBatchAsync(CancellationToken.None);
        stats!.PaidTotalAmount.Should().Be(400m, "duplicate replay double-count üretmemeli");
    }

    [Fact]
    public async Task DuplicateEventReplay_Should_NotChangeDailyStats()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 500m, paidAtUtc: paidAt);
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(eventId, occurredAt, snapshot));

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, OperationDayBounds.ToLocalDate(paidAt), "TRY");
        stats!.PaidTotalAmount.Should().Be(500m);
        stats.PaidCount.Should().Be(1);
    }

    private sealed class ProbeProcessor : IPaymentProjectionProcessor
    {
        public int Invocations { get; private set; }

        public Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(0);
        }
    }
}
