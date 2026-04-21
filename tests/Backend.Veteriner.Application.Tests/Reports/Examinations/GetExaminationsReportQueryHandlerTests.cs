using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Examinations;
using Backend.Veteriner.Application.Reports.Examinations.Queries.GetExaminationReport;
using Backend.Veteriner.Application.Reports.Examinations.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Examinations;

public sealed class GetExaminationsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetExaminationsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _examinations.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var q = new GetExaminationsReportQuery(
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
            new GetExaminationsReportQuery(
                DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), null, null, null, null, null, 1, 20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Examinations.ReportDateRangeInvalid");
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
            new GetExaminationsReportQuery(
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, clinicId, null, null, null, null, 1, 20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicContextMismatch()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var r = await CreateHandler().Handle(
            new GetExaminationsReportQuery(
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
        r.Error.Code.Should().Be("Examinations.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnTotal_AndItems_OnHappyPath()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        var apptId = Guid.NewGuid();

        _examinations.Setup(x => x.CountAsync(It.IsAny<ExaminationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);
        var ex = new Examination(
            tid,
            clinicId,
            petId,
            apptId,
            from.AddHours(4),
            "Kusma",
            "Ateş",
            null,
            null);
        _examinations
            .Setup(x => x.ListAsync(It.IsAny<ExaminationsReportFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Examination> { ex });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new GetExaminationsReportQuery(from, to, null, null, null, null, null, 1, 50),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(12);
        r.Value.Items.Should().HaveCount(1);
        r.Value.Items[0].AppointmentId.Should().Be(apptId);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmpty_When_ClientHasNoPets()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var r = await CreateHandler().Handle(
            new GetExaminationsReportQuery(
                DateTime.UtcNow.AddDays(-10),
                DateTime.UtcNow,
                null,
                clientId,
                null,
                null,
                null,
                1,
                20),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(0);
        r.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Filter_ByAppointmentId()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        _examinations.Setup(x => x.CountAsync(It.IsAny<ExaminationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var ex = new Examination(
            tid,
            clinicId,
            petId,
            appointmentId,
            from.AddDays(1),
            "A",
            "B",
            null,
            null);
        _examinations
            .Setup(x => x.ListAsync(It.IsAny<ExaminationsReportFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Examination> { ex });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new GetExaminationsReportQuery(from, to, null, null, null, appointmentId, null, 1, 50),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Items[0].ExaminationId.Should().Be(ex.Id);
    }
}
