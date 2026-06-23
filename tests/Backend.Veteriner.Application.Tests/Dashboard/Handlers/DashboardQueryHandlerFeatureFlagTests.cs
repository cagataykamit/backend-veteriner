using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;
using Backend.Veteriner.Application.Dashboard.Queries.GetSummary;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class DashboardQueryHandlerFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IDashboardTodayAppointmentStatusCountsReader> _todayCounts = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IDashboardClinicScopedReader> _clinicScoped = new();
    private readonly Mock<IDashboardAppointmentReadModelReader> _dashboardReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Fact]
    public async Task Summary_WhenFlagFalse_Should_UseCommandReaders_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _todayCounts.Setup(r => r.GetAsync(tid, cid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(1, 0, 0));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardAppointmentScheduledAtInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DateTime>());
        _clinicScoped.Setup(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _clinicScoped.Setup(r => r.CountClientsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _clinicScoped.Setup(r => r.ListRecentPetsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardRecentPetRow>());
        _clinicScoped.Setup(r => r.ListRecentClientsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardRecentClientRow>());

        await CreateSummaryHandler(false).Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        _todayCounts.Verify(r => r.GetAsync(tid, cid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _dashboardReader.Verify(r => r.GetAsync(It.IsAny<DashboardAppointmentReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Summary_WhenFlagTrue_Should_UseQueryReader_NotCommandAppointmentReaders()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _dashboardReader.Setup(r => r.GetAsync(It.IsAny<DashboardAppointmentReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardAppointmentReadResult(
                new DashboardTodayAppointmentStatusCounts(2, 1, 1),
                3,
                Array.Empty<DashboardUpcomingAppointmentRow>(),
                Enumerable.Range(0, 7).Select(i => new DashboardDailyCountDto(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-6 + i)), 0)).ToList(),
                4,
                5,
                Array.Empty<DashboardRecentPetRow>(),
                Array.Empty<DashboardRecentClientRow>()));

        await CreateSummaryHandler(true).Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        _dashboardReader.Verify(r => r.GetAsync(It.IsAny<DashboardAppointmentReadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _todayCounts.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _appointments.Verify(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicScoped.Verify(r => r.CountPetsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Summary_WhenFlagTrue_Should_PassClinicScopeToReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        DashboardAppointmentReadRequest? captured = null;
        _dashboardReader.Setup(r => r.GetAsync(It.IsAny<DashboardAppointmentReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DashboardAppointmentReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        await CreateSummaryHandler(true).Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(cid);
    }

    [Fact]
    public async Task Summary_WhenFlagTrueAndQueryReaderThrows_Should_NotFallbackToCommandDb()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _dashboardReader.Setup(r => r.GetAsync(It.IsAny<DashboardAppointmentReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateSummaryHandler(true).Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _todayCounts.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void OperationalAlertsHandler_Should_NotReferenceDashboardAppointmentReadModelReader()
    {
        typeof(GetDashboardOperationalAlertsQueryHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Should()
            .NotContain(typeof(IDashboardAppointmentReadModelReader));
    }

    [Fact]
    public void AppointmentsEnabledAndDashboardAppointmentsEnabled_Should_BeIndependentOptions()
    {
        var options = new QueryReadModelsOptions
        {
            AppointmentsEnabled = true,
            DashboardAppointmentsEnabled = false
        };

        options.AppointmentsEnabled.Should().BeTrue();
        options.DashboardAppointmentsEnabled.Should().BeFalse();
    }

    private GetDashboardSummaryQueryHandler CreateSummaryHandler(bool dashboardEnabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _appointments.Object,
            _todayCounts.Object,
            _clients.Object,
            _pets.Object,
            _clinicScoped.Object,
            _dashboardReader.Object,
            Options.Create(new QueryReadModelsOptions { DashboardAppointmentsEnabled = dashboardEnabled }));

    private static DashboardAppointmentReadResult EmptyReadResult()
        => new(
            new DashboardTodayAppointmentStatusCounts(0, 0, 0),
            0,
            Array.Empty<DashboardUpcomingAppointmentRow>(),
            Enumerable.Range(0, 7).Select(i => new DashboardDailyCountDto(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-6 + i)), 0)).ToList(),
            null,
            null,
            Array.Empty<DashboardRecentPetRow>(),
            Array.Empty<DashboardRecentClientRow>());
}
