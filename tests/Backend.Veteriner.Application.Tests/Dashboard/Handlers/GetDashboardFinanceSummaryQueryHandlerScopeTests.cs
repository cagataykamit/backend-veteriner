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
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardFinanceSummaryQueryHandlerScopeTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IDashboardFinancePaymentAggregatesReader> _aggregates = new();
    private readonly Mock<IDashboardFinanceReadModelReader> _financeReadModel = new();
    private readonly Mock<IDashboardRecentPaymentsReadModelReader> _recentReadModel = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private GetDashboardFinanceSummaryQueryHandler CreateHandler(
        bool dashboardFinanceReadEnabled = false,
        bool dashboardRecentPaymentsReadEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _aggregates.Object,
            _financeReadModel.Object,
            _recentReadModel.Object,
            Options.Create(new QueryReadModelsOptions
            {
                DashboardFinanceReadEnabled = dashboardFinanceReadEnabled,
                DashboardRecentPaymentsReadEnabled = dashboardRecentPaymentsReadEnabled
            }));

    private void SetupEmptyCommandPath()
    {
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
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceWindowTotals(0, 0, 0, 0, 0, 0));
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentPaidAtAmountRow>());
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardFinancePaymentRow>());
        _clients
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Clients.Client>());
    }

    [Fact]
    public async Task Handle_TenantWideAdmin_NoActiveClinic_Should_UseTenantWideFinancePath()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyCommandPath();
        _aggregates
            .Setup(r => r.GetTotalsAsync(
                tid,
                null,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceWindowTotals(100, 1, 0, 0, 0, 0));

        var result = await CreateHandler().Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayTotalPaid.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_TenantWideAdmin_ActiveClinic_Should_UseSingleClinicFinance()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandPath();
        _aggregates
            .Setup(r => r.GetTotalsAsync(
                tid,
                cid,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceWindowTotals(50, 1, 0, 0, 0, 0));

        var result = await CreateHandler().Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayTotalPaid.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_NonTenantWide_ActiveAssignedClinic_Should_UseSingleClinicFinance()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandPath();
        _aggregates
            .Setup(r => r.GetTotalsAsync(
                tid,
                cid,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceWindowTotals(25, 1, 0, 0, 0, 0));

        var result = await CreateHandler(scopeResolver: scope).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayTotalPaid.Should().Be(25m);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoActiveClinic_WithAssignedClinics_Should_UseAccessibleClinicIds()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, new[] { c1, c2 })));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyCommandPath();
        _aggregates
            .Setup(r => r.GetTotalsAsync(
                tid,
                null,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 2 && ids.Contains(c1) && ids.Contains(c2)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceWindowTotals(75, 2, 0, 0, 0, 0));

        var result = await CreateHandler(scopeResolver: scope).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayTotalPaid.Should().Be(75m);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoAssignments_Should_ReturnEmptyFinance_NotTenantWide()
    {
        var tid = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, Array.Empty<Guid>())));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var result = await CreateHandler(scopeResolver: scope).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayTotalPaid.Should().Be(0m);
        result.Value.RecentPayments.Should().BeEmpty();
        result.Value.Last7DaysPaid.Should().HaveCount(7).And.OnlyContain(d => d.TotalAmount == 0m);
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
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ResolverFailure_Should_Fail_WithoutReaderOrRepositoryCalls()
    {
        var tid = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Failure("Clinics.AccessDenied", "denied"));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var result = await CreateHandler(scopeResolver: scope).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
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
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _financeReadModel.Verify(
            r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_FinanceReadEnabled_Should_PassResolvedScopeToQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandPath();
        DashboardFinanceReadRequest? captured = null;
        _financeReadModel
            .Setup(r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DashboardFinanceReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        await CreateHandler(dashboardFinanceReadEnabled: true).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(cid);
        captured.AccessibleClinicIds.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FinanceReadDisabled_Should_PassResolvedScopeToCommandAggregates()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandPath();

        await CreateHandler(dashboardFinanceReadEnabled: false).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

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
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecentPayments_ReadEnabled_SingleClinic_Should_UseQueryReaderWithResolvedClinic()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandPath();
        DashboardRecentPaymentsReadRequest? captured = null;
        _recentReadModel
            .Setup(r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DashboardRecentPaymentsReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(Array.Empty<DashboardFinanceRecentPaymentDto>());

        await CreateHandler(dashboardRecentPaymentsReadEnabled: true).Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(cid);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecentPayments_ReadEnabled_MultiClinicScope_Should_FallbackToCommandWithAccessibleClinicIds()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, new[] { c1, c2 })));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyCommandPath();
        IReadOnlyCollection<Guid>? capturedAccessible = null;
        _aggregates
            .Setup(r => r.GetTotalsAsync(
                tid,
                null,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid?, DateTime, DateTime, DateTime, DateTime, DateTime, DateTime, IReadOnlyCollection<Guid>?, CancellationToken>(
                (_, _, _, _, _, _, _, _, ids, _) => capturedAccessible = ids)
            .ReturnsAsync(new DashboardFinanceWindowTotals(0, 0, 0, 0, 0, 0));

        await CreateHandler(dashboardRecentPaymentsReadEnabled: true, scopeResolver: scope)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        _recentReadModel.Verify(
            r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        capturedAccessible.Should().NotBeNull();
        capturedAccessible!.Should().BeEquivalentTo(new[] { c1, c2 });
    }

    [Fact]
    public async Task Handle_Should_PropagateCancellationToken_ToResolver()
    {
        var tid = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyCommandPath();

        await CreateHandler().Handle(new GetDashboardFinanceSummaryQuery(), cts.Token);

        _scopeResolver.Verify(
            r => r.ResolveAsync(tid, null, cts.Token),
            Times.Once);
    }

    private static DashboardFinanceReadResult EmptyReadResult()
        => new(
            new DashboardFinanceWindowTotals(0, 0, 0, 0, 0, 0),
            OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow)
                .Select(b => new DashboardDailyTotalDto(b.LocalDate, 0m))
                .ToList());
}
