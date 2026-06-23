using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetSummary;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardSummaryQueryHandlerScopeTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IDashboardTodayAppointmentStatusCountsReader> _todayCounts = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IDashboardClinicScopedReader> _clinicScoped = new();
    private readonly Mock<IDashboardAppointmentReadModelReader> _dashboardReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private GetDashboardSummaryQueryHandler CreateHandler(
        bool dashboardAppointmentsEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _appointments.Object,
            _todayCounts.Object,
            _clients.Object,
            _pets.Object,
            _clinicScoped.Object,
            _dashboardReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                DashboardAppointmentsEnabled = dashboardAppointmentsEnabled
            }));

    private void SetupEmptyCommandPath()
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
            .Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments
            .Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _appointments
            .Setup(a => a.ListAsync(It.IsAny<DashboardAppointmentScheduledAtInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DateTime>());
        _clients
            .Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets
            .Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients
            .Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _pets
            .Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());
    }

    [Fact]
    public async Task Handle_TenantWideAdmin_NoActiveClinic_Should_UseTenantWideReaders()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyCommandPath();
        _clients
            .Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _pets
            .Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await CreateHandler().Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalClientsCount.Should().Be(5);
        result.Value.TotalPetsCount.Should().Be(3);
        _clients.Verify(
            c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clinicScoped.Verify(
            r => r.CountPetsAtClinicsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoActiveClinic_WithAssignedClinics_Should_UseAccessibleClinicScope()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyCommandPath();
        _clinicScoped.Setup(r => r.CountPetsAtClinicsAsync(tid, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _clinicScoped.Setup(r => r.CountClientsAtClinicsAsync(tid, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _clinicScoped.Setup(r => r.ListRecentPetsAtClinicsAsync(tid, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardRecentPetRow>());
        _clinicScoped.Setup(r => r.ListRecentClientsAtClinicsAsync(tid, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardRecentClientRow>());

        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { c1, c2 });
        var result = await CreateHandler(scopeResolver: scopeResolver)
            .Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalPetsCount.Should().Be(2);
        _clinicScoped.Verify(r => r.CountPetsAtClinicsAsync(tid, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(
            c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        scopeResolver.Verify(
            r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoAssignments_Should_ReturnEmptyAggregate_NotTenantWide()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(Array.Empty<Guid>());
        var result = await CreateHandler(scopeResolver: scopeResolver)
            .Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayAppointmentsCount.Should().Be(0);
        result.Value.TotalClientsCount.Should().Be(0);
        result.Value.UpcomingAppointments.Should().BeEmpty();
        result.Value.Last7DaysAppointments.Should().HaveCount(7);
        _todayCounts.Verify(
            r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NonTenantWide_ActiveAssignedClinic_Should_UseSingleClinicScope()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandPath();
        _clinicScoped.Setup(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        _clinicScoped.Setup(r => r.CountClientsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _clinicScoped.Setup(r => r.ListRecentPetsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardRecentPetRow>());
        _clinicScoped.Setup(r => r.ListRecentClientsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DashboardRecentClientRow>());

        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        var result = await CreateHandler(scopeResolver: scopeResolver)
            .Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalPetsCount.Should().Be(4);
        _clinicScoped.Verify(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ResolverFailure_Should_Fail_WithoutRepositoryCalls()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _todayCounts.Verify(
            r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _dashboardReader.Verify(
            r => r.GetAsync(It.IsAny<DashboardAppointmentReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_QueryPathEnabled_Should_PassResolvedScopeToReadModelReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        DashboardAppointmentReadRequest? captured = null;
        _dashboardReader
            .Setup(r => r.GetAsync(It.IsAny<DashboardAppointmentReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DashboardAppointmentReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new DashboardAppointmentReadResult(
                new DashboardTodayAppointmentStatusCounts(0, 0, 0),
                0,
                Array.Empty<DashboardUpcomingAppointmentRow>(),
                Enumerable.Range(0, 7).Select(i => new DashboardDailyCountDto(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-6 + i)), 0)).ToList(),
                0,
                0,
                Array.Empty<DashboardRecentPetRow>(),
                Array.Empty<DashboardRecentClientRow>()));

        var cts = new CancellationTokenSource();
        await CreateHandler(dashboardAppointmentsEnabled: true)
            .Handle(new GetDashboardSummaryQuery(), cts.Token);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(cid);
        captured.AccessibleClinicIds.Should().BeNull();
        _scopeResolver.Verify(
            r => r.ResolveAsync(tid, cid, cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_PropagateCancellationToken_ToResolver()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyCommandPath();

        using var cts = new CancellationTokenSource();
        await CreateHandler().Handle(new GetDashboardSummaryQuery(), cts.Token);

        _scopeResolver.Verify(
            r => r.ResolveAsync(tid, null, cts.Token),
            Times.Once);
    }
}
