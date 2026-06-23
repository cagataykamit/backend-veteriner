using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardOperationalAlertsQueryHandlerScopeTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IDashboardTodayAppointmentStatusCountsReader> _todayCounts = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private GetDashboardOperationalAlertsQueryHandler CreateHandler(Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _appointments.Object,
            _vaccinations.Object,
            _todayCounts.Object);

    private void SetupZeroCounts()
    {
        _todayCounts
            .Setup(r => r.GetAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueScheduledAppointmentsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardUpcomingAppointmentsNext24HoursCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _vaccinations
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueVaccinationsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _vaccinations
            .Setup(r => r.CountAsync(It.IsAny<DashboardUpcomingVaccinationsNext7DaysCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    [Fact]
    public async Task Handle_TenantWideAdmin_NoActiveClinic_Should_UseTenantWideCounts()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupZeroCounts();
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueScheduledAppointmentsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await CreateHandler().Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OverdueScheduledAppointmentsCount.Should().Be(3);
        _todayCounts.Verify(
            r => r.GetAsync(tid, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoActiveClinic_WithAssignedClinics_Should_UseAccessibleScope()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupZeroCounts();
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueScheduledAppointmentsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { c1, c2 });
        var result = await CreateHandler(scopeResolver).Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _todayCounts.Verify(
            r => r.GetAsync(
                tid,
                null,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoAssignments_Should_ReturnZeros_NotTenantWide()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(Array.Empty<Guid>());
        var result = await CreateHandler(scopeResolver).Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OverdueScheduledAppointmentsCount.Should().Be(0);
        _todayCounts.Verify(
            r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveAssignedClinic_Should_UseSingleClinicScope()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupZeroCounts();

        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        await CreateHandler(scopeResolver).Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        _todayCounts.Verify(
            r => r.GetAsync(tid, cid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ResolverFailure_Should_Fail_WithoutRepositoryCalls()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _todayCounts.Verify(
            r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_PropagateCancellationToken_ToResolver()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupZeroCounts();

        using var cts = new CancellationTokenSource();
        await CreateHandler().Handle(new GetDashboardOperationalAlertsQuery(), cts.Token);

        _scopeResolver.Verify(r => r.ResolveAsync(tid, null, cts.Token), Times.Once);
    }
}
