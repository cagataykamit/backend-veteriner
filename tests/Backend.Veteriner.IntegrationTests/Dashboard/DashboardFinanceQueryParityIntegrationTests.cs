using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Dashboard;

[Collection("payment-projection")]
public sealed class DashboardFinanceQueryParityIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public DashboardFinanceQueryParityIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FinanceSummary_ClinicScoped_Should_MatchCommandPath_AfterBackfill()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicA, _) = await SeedTenantWithTwoClinicsAsync();
        var paidAt = DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow();

        await SeedPaymentAsync(tenantId, clinicA, 150m, paidAt);
        await SeedPaymentAsync(tenantId, clinicA, 250m, paidAt);
        await RunBackfillAsync(tenantId);

        var (command, query) = await DashboardFinanceQueryParityTestSupport.CompareFinancePathsAsync(
            _factory.Services, tenantId, clinicA);

        AssertFinanceParity(command, query);
    }

    [Fact]
    public async Task FinanceSummary_TenantWide_Should_MatchCommandPath_AfterBackfill()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicA, clinicB) = await SeedTenantWithTwoClinicsAsync();
        var paidAt = DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow();

        await SeedPaymentAsync(tenantId, clinicA, 100m, paidAt);
        await SeedPaymentAsync(tenantId, clinicB, 200m, paidAt);
        await RunBackfillAsync(tenantId);

        var (command, query) = await DashboardFinanceQueryParityTestSupport.CompareFinancePathsAsync(
            _factory.Services, tenantId, clinicId: null);

        AssertFinanceParity(command, query);
    }

    [Fact]
    public async Task FinanceSummary_LastSevenDays_Should_MatchCommandPath()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicId, _) = await SeedTenantWithTwoClinicsAsync();
        var buckets = OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow);

        await SeedPaymentAsync(
            tenantId,
            clinicId,
            42m,
            buckets[4].StartUtcInclusive.AddHours(10));
        await SeedPaymentAsync(
            tenantId,
            clinicId,
            100m,
            buckets[6].StartUtcInclusive.AddHours(12));
        await RunBackfillAsync(tenantId);

        var (command, query) = await DashboardFinanceQueryParityTestSupport.CompareFinancePathsAsync(
            _factory.Services, tenantId, clinicId);

        query.Last7DaysPaid.Should().BeEquivalentTo(command.Last7DaysPaid, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task FinanceSummary_WhenQueryDbEmptyAndFlagTrue_Should_ReturnZeros_NotCommandTotals()
    {
        await DashboardFinanceQueryParityTestSupport.ResetAsync(_factory.Services);
        var (tenantId, clinicId, _) = await SeedTenantWithTwoClinicsAsync();
        await SeedPaymentAsync(tenantId, clinicId, 500m, DashboardFinanceQueryParityTestSupport.TodayWithinOperationalWindow());

        var queryResult = await DashboardFinanceQueryParityTestSupport.InvokeFinanceHandlerAsync(
            _factory.Services,
            dashboardFinanceReadEnabled: true,
            tenantId,
            clinicId);

        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value!.TodayTotalPaid.Should().Be(0m);
        queryResult.Value.TodayPaymentsCount.Should().Be(0);
    }

    private static void AssertFinanceParity(DashboardFinanceSummaryDto command, DashboardFinanceSummaryDto query)
    {
        query.TodayTotalPaid.Should().Be(command.TodayTotalPaid);
        query.WeekTotalPaid.Should().Be(command.WeekTotalPaid);
        query.MonthTotalPaid.Should().Be(command.MonthTotalPaid);
        query.TodayPaymentsCount.Should().Be(command.TodayPaymentsCount);
        query.WeekPaymentsCount.Should().Be(command.WeekPaymentsCount);
        query.MonthPaymentsCount.Should().Be(command.MonthPaymentsCount);
        query.Last7DaysPaid.Should().BeEquivalentTo(command.Last7DaysPaid, options => options.WithStrictOrdering());
    }

    private async Task<(Guid TenantId, Guid ClinicA, Guid ClinicB)> SeedTenantWithTwoClinicsAsync()
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
}
