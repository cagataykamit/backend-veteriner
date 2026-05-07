using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Appointments.Queries.GetAppointmentReport;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Appointments;

public sealed class GetAppointmentsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IAppointmentsReportStatusBreakdownReader> _statusBreakdown = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private GetAppointmentsReportQueryHandler CreateHandler()
        => new(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _appointments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _statusBreakdown.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var q = new GetAppointmentsReportQuery(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, null, null, null, null, 1, 20);
        var r = await CreateHandler().Handle(q, CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_FromAfterTo()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var r = await CreateHandler().Handle(
            new GetAppointmentsReportQuery(
                DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), null, null, null, null, null, 1, 20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Appointments.ReportDateRangeInvalid");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicNotInTenant()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _scopeResolver.SetupNotFound();
        var r = await CreateHandler().Handle(
            new GetAppointmentsReportQuery(
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, clinicId, null, null, null, null, 1, 20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ApplyStatusFilter_And_ReturnTotal()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        var petId = Guid.NewGuid();

        _statusBreakdown
            .Setup(x => x.GetAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyList<Guid>?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<Guid[]>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentStatusCountRow>
            {
                new(AppointmentStatus.Completed, 7),
            });
        var ap = new Appointment(tid, clinicId, petId, from.AddHours(2), initialStatus: AppointmentStatus.Completed);
        _appointments
            .Setup(x => x.ListAsync(It.IsAny<AppointmentsReportFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { ap });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new GetAppointmentsReportQuery(
                from,
                to,
                null,
                AppointmentStatus.Completed,
                null,
                null,
                null,
                1,
                50),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(7);
        r.Value.Items.Should().HaveCount(1);
        r.Value.StatusCounts.Completed.Should().Be(7);
    }

    [Fact]
    public async Task Handle_Should_SumTotal_When_StatusFilterNull()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        var petId = Guid.NewGuid();

        _statusBreakdown
            .Setup(x => x.GetAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyList<Guid>?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<Guid[]>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentStatusCountRow>
            {
                new(AppointmentStatus.Scheduled, 2),
                new(AppointmentStatus.Completed, 5),
                new(AppointmentStatus.Cancelled, 1),
            });
        var ap = new Appointment(tid, clinicId, petId, from.AddHours(2), initialStatus: AppointmentStatus.Scheduled);
        _appointments
            .Setup(x => x.ListAsync(It.IsAny<AppointmentsReportFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { ap });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new GetAppointmentsReportQuery(
                from,
                to,
                null,
                null,
                null,
                null,
                null,
                1,
                50),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(8);
        r.Value.StatusCounts.Scheduled.Should().Be(2);
        r.Value.StatusCounts.Completed.Should().Be(5);
        r.Value.StatusCounts.Cancelled.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicContextMismatch()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var r = await CreateHandler().Handle(
            new GetAppointmentsReportQuery(
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow,
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                1,
                20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Appointments.ClinicContextMismatch");
    }
}
