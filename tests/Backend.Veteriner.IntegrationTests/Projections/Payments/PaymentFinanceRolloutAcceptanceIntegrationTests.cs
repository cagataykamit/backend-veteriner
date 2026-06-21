using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Dashboard;
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

    [Fact]
    public async Task BothFlagsDisabled_Should_PreserveCommandDbDashboardFinance_WithPaymentsPresent()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicId, _) = await SeedTenantAsync();
        var paidAt = DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow();
        await SeedPaymentAsync(tenantId, clinicId, 275m, paidAt);

        var commandResult = await DashboardFinanceQueryParityTestSupport.InvokeFinanceHandlerAsync(
            _factory.Services,
            dashboardFinanceReadEnabled: false,
            tenantId,
            clinicId);

        commandResult.IsSuccess.Should().BeTrue();
        commandResult.Value!.TodayTotalPaid.Should().Be(275m);
        commandResult.Value.TodayPaymentsCount.Should().Be(1);
    }

    [Fact]
    public async Task AfterBackfill_DashboardFinanceReadEnabledTrue_Should_ParityWithCommandDb()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicId, _) = await SeedTenantAsync();
        var paidAt = DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow();
        await SeedPaymentAsync(tenantId, clinicId, 180m, paidAt);
        await SeedPaymentAsync(tenantId, clinicId, 120m, paidAt);
        await RunBackfillAsync(tenantId);

        var (command, query) = await DashboardFinanceQueryParityTestSupport.CompareFinancePathsAsync(
            _factory.Services, tenantId, clinicId);

        AssertDashboardFinanceParity(command, query);
    }

    [Fact]
    public async Task AfterBackfill_ParityReader_Should_ReportInSync()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicId, _) = await SeedTenantAsync();
        await SeedPaymentAsync(
            tenantId,
            clinicId,
            300m,
            DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow());
        await RunBackfillAsync(tenantId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var parity = scope.ServiceProvider.GetRequiredService<IPaymentFinanceParityReader>();
        var result = await parity.GetTenantParityAsync(tenantId, CancellationToken.None);

        result.CountInSync.Should().BeTrue();
        result.DailyBucketParityInSync.Should().BeTrue();
        result.InSync.Should().BeTrue();
    }

    [Fact]
    public async Task Rollback_DisablingDashboardFinanceRead_Should_MatchCommandDbAfterBackfill()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicId, _) = await SeedTenantAsync();
        await SeedPaymentAsync(
            tenantId,
            clinicId,
            450m,
            DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow());
        await RunBackfillAsync(tenantId);

        var queryEnabled = await DashboardFinanceQueryParityTestSupport.InvokeFinanceHandlerAsync(
            _factory.Services, dashboardFinanceReadEnabled: true, tenantId, clinicId);
        var rollback = await DashboardFinanceQueryParityTestSupport.InvokeFinanceHandlerAsync(
            _factory.Services, dashboardFinanceReadEnabled: false, tenantId, clinicId);

        queryEnabled.IsSuccess.Should().BeTrue();
        rollback.IsSuccess.Should().BeTrue();
        rollback.Value!.TodayTotalPaid.Should().Be(queryEnabled.Value!.TodayTotalPaid);
        rollback.Value.TodayPaymentsCount.Should().Be(queryEnabled.Value.TodayPaymentsCount);
        rollback.Value.WeekTotalPaid.Should().Be(queryEnabled.Value.WeekTotalPaid);
        rollback.Value.MonthTotalPaid.Should().Be(queryEnabled.Value.MonthTotalPaid);
    }

    [Fact]
    public async Task QueryDbEmptyWithReadFlagTrue_Should_ReturnZeroTotals_NotCommandDbTotals()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicId, _) = await SeedTenantAsync();
        await SeedPaymentAsync(
            tenantId,
            clinicId,
            999m,
            DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow());

        var queryResult = await DashboardFinanceQueryParityTestSupport.InvokeFinanceHandlerAsync(
            _factory.Services, dashboardFinanceReadEnabled: true, tenantId, clinicId);
        var commandResult = await DashboardFinanceQueryParityTestSupport.InvokeFinanceHandlerAsync(
            _factory.Services, dashboardFinanceReadEnabled: false, tenantId, clinicId);

        queryResult.Value!.TodayTotalPaid.Should().Be(0m);
        commandResult.Value!.TodayTotalPaid.Should().Be(999m);
    }

    private static void AssertDashboardFinanceParity(DashboardFinanceSummaryDto command, DashboardFinanceSummaryDto query)
    {
        query.TodayTotalPaid.Should().Be(command.TodayTotalPaid);
        query.WeekTotalPaid.Should().Be(command.WeekTotalPaid);
        query.MonthTotalPaid.Should().Be(command.MonthTotalPaid);
        query.TodayPaymentsCount.Should().Be(command.TodayPaymentsCount);
        query.WeekPaymentsCount.Should().Be(command.WeekPaymentsCount);
        query.MonthPaymentsCount.Should().Be(command.MonthPaymentsCount);
        query.Last7DaysPaid.Should().BeEquivalentTo(command.Last7DaysPaid, options => options.WithStrictOrdering());
    }

    private async Task<(Guid TenantId, Guid ClinicA, Guid ClinicB)> SeedTenantAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
    }

    private async Task SeedPaymentAsync(Guid tenantId, Guid clinicId, decimal amount, DateTime paidAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicId, amount, paidAtUtc);
    }

    private async Task RunBackfillAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentFinanceBackfillService>();
        var result = await backfill.BackfillAsync(tenantId, PaymentFinanceBackfillService.DefaultBatchSize, CancellationToken.None);
        result.Success.Should().BeTrue();
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
