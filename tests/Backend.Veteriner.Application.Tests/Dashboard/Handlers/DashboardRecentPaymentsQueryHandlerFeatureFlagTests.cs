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

/// <summary>
/// CQRS-15B: <see cref="QueryReadModelsOptions.DashboardRecentPaymentsReadEnabled"/> routing for dashboard recent payments.
/// </summary>
public sealed class DashboardRecentPaymentsQueryHandlerFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IDashboardFinancePaymentAggregatesReader> _aggregates = new();
    private readonly Mock<IDashboardFinanceReadModelReader> _financeReadModel = new();
    private readonly Mock<IDashboardRecentPaymentsReadModelReader> _recentReadModel = new();

    [Fact]
    public async Task RecentPayments_WhenFlagFalse_Should_UseCommandDb_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupFinanceCommandPath();
        SetupEmptyCommandRecent();

        await CreateHandler(dashboardRecentPaymentsReadEnabled: false)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _recentReadModel.Verify(
            r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _scopeResolver.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecentPayments_WhenFlagTrue_AndSingleClinicScope_Should_UseQueryReader_NotCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupFinanceCommandPath();
        _recentReadModel
            .Setup(r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardFinanceRecentPaymentDto>());

        await CreateHandler(dashboardRecentPaymentsReadEnabled: true)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        _recentReadModel.Verify(
            r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecentPayments_WhenFlagTrue_AndQueryDbEmpty_Should_ReturnEmptyRecent_WithoutCommandFallback()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupFinanceCommandPath();
        _recentReadModel
            .Setup(r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardFinanceRecentPaymentDto>());

        var result = await CreateHandler(dashboardRecentPaymentsReadEnabled: true)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecentPayments.Should().BeEmpty();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecentPayments_WhenFlagTrue_AndTenantWideScope_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupFinanceCommandPath();
        SetupEmptyCommandRecent();

        await CreateHandler(dashboardRecentPaymentsReadEnabled: true)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        _recentReadModel.Verify(
            r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecentPayments_WhenFlagTrue_AndMultiClinicScope_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _scopeResolver.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, new[] { c1, c2 })));
        SetupFinanceCommandPath();
        SetupEmptyCommandRecent();

        await CreateHandler(dashboardRecentPaymentsReadEnabled: true, scopeResolver: _scopeResolver)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        _recentReadModel.Verify(
            r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecentPayments_WhenQueryPath_Should_PassTenantClinicScopeAndTakeToReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupFinanceCommandPath();

        DashboardRecentPaymentsReadRequest? captured = null;
        _recentReadModel
            .Setup(r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DashboardRecentPaymentsReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(Array.Empty<DashboardFinanceRecentPaymentDto>());

        await CreateHandler(dashboardRecentPaymentsReadEnabled: true)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(cid);
        captured.Take.Should().Be(DashboardFinanceSummaryConstants.RecentPaymentsTake);
    }

    [Fact]
    public async Task RecentPayments_WhenQueryPath_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupFinanceCommandPath();
        _recentReadModel
            .Setup(r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(dashboardRecentPaymentsReadEnabled: true)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecentPayments_WhenQueryPath_Should_MapReaderItemsDirectly_WithoutClientPetLookup()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var paidAt = DateTime.UtcNow.AddMinutes(-5);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupFinanceCommandPath();

        var item = new DashboardFinanceRecentPaymentDto(
            Guid.NewGuid(),
            paidAt,
            clientId,
            "Denormalize Client",
            petId,
            "Denormalize Pet",
            99m,
            "TRY",
            PaymentMethod.Cash);
        _recentReadModel
            .Setup(r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });

        var result = await CreateHandler(dashboardRecentPaymentsReadEnabled: true)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var recent = result.Value!.RecentPayments.Should().ContainSingle().Subject;
        recent.ClientName.Should().Be("Denormalize Client");
        recent.PetName.Should().Be("Denormalize Pet");
        recent.Amount.Should().Be(99m);
    }

    [Fact]
    public async Task FinanceTotals_WhenDashboardFinanceReadEnabled_Should_RemainIndependentFromRecentFlag()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandRecent();
        _financeReadModel
            .Setup(r => r.GetAsync(It.IsAny<DashboardFinanceReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceReadResult(
                new DashboardFinanceWindowTotals(5, 1, 0, 0, 0, 0),
                OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow)
                    .Select(b => new DashboardDailyTotalDto(b.LocalDate, 0m))
                    .ToList()));
        _recentReadModel
            .Setup(r => r.GetRecentAsync(It.IsAny<DashboardRecentPaymentsReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardFinanceRecentPaymentDto>());

        var result = await CreateHandler(
                dashboardFinanceReadEnabled: true,
                dashboardRecentPaymentsReadEnabled: true)
            .Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayTotalPaid.Should().Be(5m);
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
    }

    [Fact]
    public void DashboardRecentPaymentsReadEnabled_Should_BeIndependentFromDashboardFinanceReadEnabled()
    {
        var options = new QueryReadModelsOptions
        {
            DashboardRecentPaymentsReadEnabled = true,
            DashboardFinanceReadEnabled = false
        };
        options.DashboardRecentPaymentsReadEnabled.Should().BeTrue();
        options.DashboardFinanceReadEnabled.Should().BeFalse();
    }

    private void SetupFinanceCommandPath()
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
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardFinanceWindowTotals(0, 0, 0, 0, 0, 0));
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentPaidAtAmountRow>());
    }

    private void SetupEmptyCommandRecent()
    {
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardFinancePaymentRow>());
        _clients
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Clients.Client>());
    }

    private GetDashboardFinanceSummaryQueryHandler CreateHandler(
        bool dashboardFinanceReadEnabled = false,
        bool dashboardRecentPaymentsReadEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
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
}
