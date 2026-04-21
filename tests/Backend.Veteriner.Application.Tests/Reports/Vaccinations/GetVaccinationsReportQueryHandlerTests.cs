using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Vaccinations;
using Backend.Veteriner.Application.Reports.Vaccinations.Queries.GetVaccinationReport;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Vaccinations;

public sealed class GetVaccinationsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetVaccinationsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _vaccinations.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var r = await CreateHandler().Handle(
            new GetVaccinationsReportQuery(
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, null, null, null, null, 1, 20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_FromAfterTo()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var r = await CreateHandler().Handle(
            new GetVaccinationsReportQuery(
                DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), null, null, null, null, null, 1, 20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.ReportDateRangeInvalid");
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
            new GetVaccinationsReportQuery(
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, clinicId, null, null, null, null, 1, 20),
            CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicContextMismatch()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinic.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var r = await CreateHandler().Handle(
            new GetVaccinationsReportQuery(
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
        r.Error.Code.Should().Be("Vaccinations.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnTotal_And_FilterByStatus()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        var applied = from.AddDays(5);

        _vaccinations.Setup(x => x.CountAsync(It.IsAny<VaccinationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        var vac = new Vaccination(
            tid,
            petId,
            clinicId,
            null,
            "Karma",
            VaccinationStatus.Applied,
            applied,
            from.AddDays(60),
            null);
        _vaccinations
            .Setup(x => x.ListAsync(It.IsAny<VaccinationsReportFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination> { vac });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new GetVaccinationsReportQuery(
                from,
                to,
                null,
                VaccinationStatus.Applied,
                null,
                null,
                null,
                1,
                50),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(5);
        r.Value.Items.Should().HaveCount(1);
        r.Value.Items[0].Status.Should().Be(VaccinationStatus.Applied);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmpty_When_ClientHasNoPets()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var r = await CreateHandler().Handle(
            new GetVaccinationsReportQuery(
                DateTime.UtcNow.AddDays(-10),
                DateTime.UtcNow,
                null,
                null,
                clientId,
                null,
                null,
                1,
                20),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(0);
    }
}
