using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Payments;

[Collection("payment-projection")]
public sealed class PaymentFinanceParityIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentFinanceParityIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task TenantParity_Should_BeInSync_AfterBackfill()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentFinanceBackfillService>();
        var parity = scope.ServiceProvider.GetRequiredService<IPaymentFinanceParityReader>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Payments.ExecuteDeleteAsync();

        var (tenantId, clinicId, _) =
            await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        var paidAt = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedPaymentAsync(
            commandDb, tenantId, clinicId, 100m, paidAt);
        await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedPaymentAsync(
            commandDb, tenantId, clinicId, 200m, paidAt);

        await backfill.BackfillAsync(tenantId, cancellationToken: CancellationToken.None);

        var result = await parity.GetTenantParityAsync(tenantId);
        result.CommandPaymentCount.Should().Be(2);
        result.QueryContributionCount.Should().Be(2);
        result.CountInSync.Should().BeTrue();
        result.DailyBucketParityInSync.Should().BeTrue();
        result.InSync.Should().BeTrue();
    }

    [Fact]
    public async Task TenantParity_Should_ReportContributionBehind_WhenNotBackfilledOrProjected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var parity = scope.ServiceProvider.GetRequiredService<IPaymentFinanceParityReader>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Payments.ExecuteDeleteAsync();

        var (tenantId, clinicId, _) =
            await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedPaymentAsync(
            commandDb, tenantId, clinicId, 150m, DateTime.UtcNow);

        var result = await parity.GetTenantParityAsync(tenantId);
        result.CommandPaymentCount.Should().Be(1);
        result.QueryContributionCount.Should().Be(0);
        result.CountInSync.Should().BeFalse();
        result.InSync.Should().BeFalse();
    }

    [Fact]
    public async Task TenantParity_Should_IsolateTenants()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentFinanceBackfillService>();
        var parity = scope.ServiceProvider.GetRequiredService<IPaymentFinanceParityReader>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Payments.ExecuteDeleteAsync();

        var (tenantA, clinicA, _) =
            await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        var (tenantB, clinicB, _) =
            await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        var paidAt = new DateTime(2026, 6, 18, 8, 0, 0, DateTimeKind.Utc);

        await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedPaymentAsync(
            commandDb, tenantA, clinicA, 100m, paidAt);
        await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedPaymentAsync(
            commandDb, tenantB, clinicB, 200m, paidAt);
        await Backend.IntegrationTests.Projections.Payments.PaymentFinanceTestSupport.SeedPaymentAsync(
            commandDb, tenantB, clinicB, 300m, paidAt);

        await backfill.BackfillAsync(tenantA, cancellationToken: CancellationToken.None);

        (await parity.GetTenantParityAsync(tenantA)).InSync.Should().BeTrue();
        (await parity.GetTenantParityAsync(tenantB)).InSync.Should().BeFalse();
    }
}
