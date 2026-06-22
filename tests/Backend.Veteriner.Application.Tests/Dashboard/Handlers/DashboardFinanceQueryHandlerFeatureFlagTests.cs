using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class DashboardFinanceQueryHandlerFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IDashboardFinancePaymentAggregatesReader> _aggregates = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IDashboardFinanceReadModelReader> _financeReadModel = new();
    private readonly Mock<IDashboardRecentPaymentsReadModelReader> _recentReadModel = new();

    [Fact]
    public async Task FinanceSummary_WhenFlagFalse_Should_UseCommandAggregates_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyRecentPayments();
        _aggregates
            .Setup(r => r.GetTotalsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceWindowTotals(0, 0, 0, 0, 0, 0));
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentPaidAtAmountRow>());

        await CreateHandler(false).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        _aggregates.Verify(
            r => r.GetTotalsAsync(
                tid,
                cid,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _financeReadModel.Verify(
            r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinanceSummary_WhenFlagTrue_Should_UseQueryReader_NotCommandAggregates()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyRecentPayments();
        _financeReadModel
            .Setup(r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceReadResult(
                new DashboardFinanceWindowTotals(10, 1, 20, 2, 30, 3),
                OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow)
                    .Select(b => new DashboardDailyTotalDto(b.LocalDate, 0m))
                    .ToList()));

        var result = await CreateHandler(true).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayTotalPaid.Should().Be(10m);
        _financeReadModel.Verify(
            r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _aggregates.Verify(
            r => r.GetTotalsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinanceSummary_WhenFlagTrue_Should_PassTenantAndClinicScopeToReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyRecentPayments();
        DashboardFinanceReadRequest? captured = null;
        _financeReadModel
            .Setup(r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DashboardFinanceReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        await CreateHandler(true).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(cid);
    }

    [Fact]
    public async Task FinanceSummary_WhenFlagTrueAndQueryReaderThrows_Should_NotFallbackToCommandDb()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _financeReadModel
            .Setup(r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(true).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _aggregates.Verify(
            r => r.GetTotalsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FinanceSummary_WhenFlagTrueAndQueryDbEmpty_Should_ReturnZeroTotals()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyRecentPayments();
        _financeReadModel
            .Setup(r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyReadResult());

        var result = await CreateHandler(true).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var v = result.Value!;
        v.TodayTotalPaid.Should().Be(0m);
        v.WeekTotalPaid.Should().Be(0m);
        v.MonthTotalPaid.Should().Be(0m);
        v.TodayPaymentsCount.Should().Be(0);
        v.Last7DaysPaid.Should().HaveCount(7);
        v.Last7DaysPaid.Should().OnlyContain(d => d.TotalAmount == 0m);
    }

    [Fact]
    public void DashboardFinanceReadEnabled_Should_BeIndependentFromPaymentProjectionEnabled()
    {
        var options = new QueryReadModelsOptions { DashboardFinanceReadEnabled = true };
        options.DashboardFinanceReadEnabled.Should().BeTrue();
        options.DashboardAppointmentsEnabled.Should().BeFalse();
    }

    private GetDashboardFinanceSummaryQueryHandler CreateHandler(bool dashboardFinanceReadEnabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _aggregates.Object,
            _financeReadModel.Object,
            _recentReadModel.Object,
            Options.Create(new QueryReadModelsOptions { DashboardFinanceReadEnabled = dashboardFinanceReadEnabled }));

    private void SetupEmptyRecentPayments()
    {
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardFinancePaymentRow>());
        _clients
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Clients.Client>());
    }

    private static DashboardFinanceReadResult EmptyReadResult()
        => new(
            new DashboardFinanceWindowTotals(0, 0, 0, 0, 0, 0),
            OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow)
                .Select(b => new DashboardDailyTotalDto(b.LocalDate, 0m))
                .ToList());
}
