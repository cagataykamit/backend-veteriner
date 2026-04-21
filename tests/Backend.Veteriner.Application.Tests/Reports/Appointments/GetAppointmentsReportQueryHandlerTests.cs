using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Queries.GetAppointmentReport;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
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

    private GetAppointmentsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _appointments.Object, _clients.Object, _pets.Object, _clinics.Object);

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
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);
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

        _appointments.Setup(x => x.CountAsync(It.IsAny<AppointmentsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
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
