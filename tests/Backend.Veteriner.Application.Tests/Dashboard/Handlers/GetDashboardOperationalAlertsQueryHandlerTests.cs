using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardOperationalAlertsQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IDashboardTodayAppointmentStatusCountsReader> _todayCounts = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private GetDashboardOperationalAlertsQueryHandler CreateHandler()
        => new(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _appointments.Object,
            _vaccinations.Object,
            _pets.Object,
            _todayCounts.Object,
            TimeProvider.System);

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
            .Setup(r => r.GetAsync(tenantId, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetAsync(tenantId, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
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
    public void OverdueScheduledAppointmentsSpec_Should_Exclude_Deceased_Pets_Appointments()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        var deceasedPetId = Guid.NewGuid();
        var activePetId = Guid.NewGuid();
        var rows = new List<Appointment>
        {
            new(tenantId, clinicId, activePetId, nowUtc.AddHours(-2)),
            new(tenantId, clinicId, deceasedPetId, nowUtc.AddHours(-2)),
            new(tenantId, clinicId, activePetId, nowUtc.AddHours(2)),
        };

        var withExclusion = new DashboardOverdueScheduledAppointmentsCountSpec(
            tenantId, clinicId, nowUtc, null, [deceasedPetId]).Evaluate(rows);
        var withoutExclusion = new DashboardOverdueScheduledAppointmentsCountSpec(tenantId, clinicId, nowUtc).Evaluate(rows);

        withExclusion.Select(a => a.PetId).Should().Equal(activePetId);
        withoutExclusion.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_Should_Pass_Deceased_Pet_Ids_To_Overdue_Appointments_Count()
    {
        var tenantId = Guid.NewGuid();
        var deceasedPetId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _todayCounts
            .Setup(r => r.GetAsync(tenantId, null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _pets.Setup(r => r.ListAsync(It.IsAny<Ardalis.Specification.ISpecification<Pet, Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([deceasedPetId]);
        DashboardOverdueScheduledAppointmentsCountSpec? captured = null;
        _appointments
            .Setup(r => r.CountAsync(It.IsAny<DashboardOverdueScheduledAppointmentsCountSpec>(), It.IsAny<CancellationToken>()))
            .Callback((Ardalis.Specification.ISpecification<Appointment> s, CancellationToken _) =>
                captured = (DashboardOverdueScheduledAppointmentsCountSpec)s)
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var past = DateTime.UtcNow.AddHours(-1);
        captured!.Evaluate(
            [
                new Appointment(tenantId, Guid.NewGuid(), deceasedPetId, past),
                new Appointment(tenantId, Guid.NewGuid(), Guid.NewGuid(), past),
            ])
            .Should().ContainSingle(a => a.PetId != deceasedPetId);
    }

    [Fact]
    public async Task Handle_Should_UseClinicScope_When_ClinicSelected()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _todayCounts
            .Setup(r => r.GetAsync(tenantId, clinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
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
            r => r.GetAsync(tenantId, clinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
