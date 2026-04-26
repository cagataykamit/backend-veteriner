using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardOperationalAlertsQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IDashboardTodayAppointmentStatusCountsReader> _todayCounts = new();

    private GetDashboardOperationalAlertsQueryHandler CreateHandler()
        => new(
            _tenant.Object,
            _clinic.Object,
            _appointments.Object,
            _vaccinations.Object,
            _todayCounts.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnZeros_When_NoData()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _todayCounts
            .Setup(r => r.GetAsync(tenantId, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.OverdueScheduledAppointmentsCount.Should().Be(0);
        dto.UpcomingAppointmentsNext24HoursCount.Should().Be(0);
        dto.TodayCancelledAppointmentsCount.Should().Be(0);
        dto.OverdueVaccinationsCount.Should().Be(0);
        dto.UpcomingVaccinationsNext7DaysCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Should_MapAllCounts()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _todayCounts
            .Setup(r => r.GetAsync(tenantId, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(4, 1, 3));
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueScheduledAppointmentsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardUpcomingAppointmentsNext24HoursCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _vaccinations
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueVaccinationsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        _vaccinations
            .Setup(r => r.CountAsync(It.IsAny<DashboardUpcomingVaccinationsNext7DaysCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.OverdueScheduledAppointmentsCount.Should().Be(2);
        dto.UpcomingAppointmentsNext24HoursCount.Should().Be(5);
        dto.TodayCancelledAppointmentsCount.Should().Be(3);
        dto.OverdueVaccinationsCount.Should().Be(7);
        dto.UpcomingVaccinationsNext7DaysCount.Should().Be(11);
    }

    [Fact]
    public async Task Handle_Should_UseClinicScope_When_ClinicSelected()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _todayCounts
            .Setup(r => r.GetAsync(tenantId, clinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 1));
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueScheduledAppointmentsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardUpcomingAppointmentsNext24HoursCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _vaccinations
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueVaccinationsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _vaccinations
            .Setup(r => r.CountAsync(It.IsAny<DashboardUpcomingVaccinationsNext7DaysCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _todayCounts.Verify(
            r => r.GetAsync(tenantId, clinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
