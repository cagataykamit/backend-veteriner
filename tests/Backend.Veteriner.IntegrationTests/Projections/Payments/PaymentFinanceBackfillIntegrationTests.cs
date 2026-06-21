using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Payments;

[Collection("payment-projection")]
public sealed class PaymentFinanceBackfillIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentFinanceBackfillIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Backfill_WithEmptyQueryDb_Should_FillContributionsAndDailyStats_AndBeInSync()
    {
        await ResetAsync();
        var (tenantId, clinicA) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        await SeedPaymentAsync(tenantId, clinicA, 100m, paidAt);
        await SeedPaymentAsync(tenantId, clinicA, 150m, paidAt);
        await SeedPaymentAsync(tenantId, clinicA, 50m, paidAt);

        var result = await RunBackfillAsync(tenantId);

        result.Success.Should().BeTrue();
        result.CommandPaymentCount.Should().Be(3);
        result.QueryContributionCount.Should().Be(3);
        result.InsertedCount.Should().Be(3);
        result.CountParityInSync.Should().BeTrue();
        result.DailyBucketParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var localDate = OperationDayBounds.ToLocalDate(paidAt);
        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(queryDb, tenantId, clinicA, localDate, "TRY");
        stats!.PaidTotalAmount.Should().Be(300m);
        stats.PaidCount.Should().Be(3);
    }

    [Fact]
    public async Task Backfill_RunTwice_Should_BeIdempotent_WithoutDoubleCount()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        await SeedPaymentAsync(tenantId, clinicId, 200m, paidAt);

        var first = await RunBackfillAsync(tenantId);
        var second = await RunBackfillAsync(tenantId);

        first.InsertedCount.Should().Be(1);
        second.InsertedCount.Should().Be(0);
        second.UpdatedCount.Should().Be(1);
        second.CountParityInSync.Should().BeTrue();
        second.DailyBucketParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.PaymentDailyContributionReadModels.CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, OperationDayBounds.ToLocalDate(paidAt), "TRY");
        stats!.PaidTotalAmount.Should().Be(200m, "re-run double-count üretmemeli");
        stats.PaidCount.Should().Be(1);
    }

    [Fact]
    public async Task Backfill_TenantScoped_Should_IsolateTenants()
    {
        await ResetAsync();
        var (tenantA, clinicA) = await SeedTenantAsync();
        var (tenantB, clinicB) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 19, 8, 0, 0, DateTimeKind.Utc);

        await SeedPaymentAsync(tenantA, clinicA, 100m, paidAt);
        await SeedPaymentAsync(tenantA, clinicA, 120m, paidAt);
        await SeedPaymentAsync(tenantB, clinicB, 300m, paidAt);

        var result = await RunBackfillAsync(tenantA);

        result.ScopeTenantId.Should().Be(tenantA);
        result.CommandPaymentCount.Should().Be(2);
        result.QueryContributionCount.Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.PaymentDailyContributionReadModels.CountAsync(x => x.TenantId == tenantA)).Should().Be(2);
        (await queryDb.PaymentDailyContributionReadModels.CountAsync(x => x.TenantId == tenantB)).Should().Be(0);
    }

    [Fact]
    public async Task Backfill_Should_SeparateClinicDailyBuckets()
    {
        await ResetAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenantId, clinicA, clinicB) = await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        var paidAt = new DateTime(2026, 6, 21, 14, 0, 0, DateTimeKind.Utc);
        var localDate = OperationDayBounds.ToLocalDate(paidAt);

        await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicA, 100m, paidAt);
        await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicB, 250m, paidAt);

        var result = await RunBackfillAsync(tenantId);
        result.DailyBucketParityInSync.Should().BeTrue();

        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var statsA = await PaymentProjectionTestSupport.FindDailyStatsAsync(queryDb, tenantId, clinicA, localDate, "TRY");
        var statsB = await PaymentProjectionTestSupport.FindDailyStatsAsync(queryDb, tenantId, clinicB, localDate, "TRY");
        statsA!.PaidTotalAmount.Should().Be(100m);
        statsB!.PaidTotalAmount.Should().Be(250m);
    }

    [Fact]
    public async Task Backfill_ThenProjectionEvent_Should_NotDoubleCount()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc);
        var seed = await SeedPaymentAsync(tenantId, clinicId, 175m, paidAt);

        await RunBackfillAsync(tenantId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        var eventId = Guid.NewGuid();
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(
            seed.PaymentId, tenantId, clinicId, amount: 175m, paidAtUtc: paidAt);
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, seed.LocalDate, "TRY");
        stats!.PaidTotalAmount.Should().Be(175m);
        stats.PaidCount.Should().Be(1);
        (await queryDb.PaymentDailyContributionReadModels.CountAsync(x => x.PaymentId == seed.PaymentId)).Should().Be(1);
    }

    [Fact]
    public async Task Backfill_Should_SkipStale_WhenNewerProjectionEventExists()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 23, 11, 0, 0, DateTimeKind.Utc);
        var seed = await SeedPaymentAsync(tenantId, clinicId, 100m, paidAt);

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        var updatedSnapshot = PaymentProjectionTestSupport.CreateSnapshot(
            seed.PaymentId, tenantId, clinicId, amount: 999m, paidAtUtc: paidAt);
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, updatedSnapshot));
        await processor.ProcessBatchAsync(CancellationToken.None);

        var result = await RunBackfillAsync(tenantId);
        result.SkippedStaleCount.Should().BeGreaterThanOrEqualTo(1);

        var contribution = await queryDb.PaymentDailyContributionReadModels.SingleAsync(x => x.PaymentId == seed.PaymentId);
        contribution.Amount.Should().Be(999m, "backfill daha yeni event'i ezmemeli");
    }

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Payments.ExecuteDeleteAsync();
    }

    private async Task<(Guid TenantId, Guid ClinicId)> SeedTenantAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenantId, clinicId, _) = await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        return (tenantId, clinicId);
    }

    private async Task<PaymentFinanceTestSupport.PaymentSeed> SeedPaymentAsync(
        Guid tenantId,
        Guid clinicId,
        decimal amount,
        DateTime paidAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicId, amount, paidAtUtc);
    }

    private async Task<PaymentFinanceBackfillResult> RunBackfillAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentFinanceBackfillService>();
        return await backfill.BackfillAsync(tenantId, batchSize: 100, CancellationToken.None);
    }
}
