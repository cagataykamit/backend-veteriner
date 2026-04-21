using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Vaccinations;
using Backend.Veteriner.Application.Reports.Vaccinations.Queries.ExportVaccinationReport;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Vaccinations;

public sealed class ExportVaccinationsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportVaccinationsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _vaccinations.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_ReturnCsv_WithTurkishHeaders()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);
        var applied = from.AddDays(2);
        var vac = new Vaccination(
            tid,
            petId,
            clinicId,
            null,
            "Felis",
            VaccinationStatus.Applied,
            applied,
            null,
            null);

        _vaccinations.Setup(x => x.CountAsync(It.IsAny<VaccinationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _vaccinations
            .Setup(x => x.ListAsync(It.IsAny<VaccinationsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination> { vac });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new ExportVaccinationsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom);
        text.Should().Contain("Uygulama Tarihi");
        text.Should().Contain("Uygulandı");
        text.Should().NotContain(vac.Id.ToString("D"));
        r.Value.FileDownloadName.Should().StartWith("asi-raporu-");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ExportTooManyRows()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _vaccinations.Setup(x => x.CountAsync(It.IsAny<VaccinationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VaccinationsReportConstants.MaxExportRows + 1);

        var r = await CreateHandler().Handle(
            new ExportVaccinationsReportQuery(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);

        r.Error.Code.Should().Be("Vaccinations.ReportExportTooManyRows");
    }

    [Fact]
    public async Task Handle_Should_NotLeakForeignGuid()
    {
        var tid = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _vaccinations.Setup(x => x.CountAsync(It.IsAny<VaccinationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var vac = new Vaccination(
            tid,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "X",
            VaccinationStatus.Scheduled,
            null,
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc),
            null);
        _vaccinations
            .Setup(x => x.ListAsync(It.IsAny<VaccinationsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination> { vac });
        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new ExportVaccinationsReportQuery(
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom).Should().NotContain(foreign.ToString("D"));
    }

    [Fact]
    public async Task Handle_Should_HandleNullNotes_And_Applied()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var vac = new Vaccination(
            tid,
            petId,
            clinicId,
            null,
            "Rabies",
            VaccinationStatus.Applied,
            from.AddHours(2),
            null,
            null);

        _vaccinations.Setup(x => x.CountAsync(It.IsAny<VaccinationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _vaccinations
            .Setup(x => x.ListAsync(It.IsAny<VaccinationsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination> { vac });
        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(petId, Guid.NewGuid(), "Z") });
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow> { new(clinicId, "Cl") });

        var r = await CreateHandler().Handle(
            new ExportVaccinationsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom);
        text.Should().Contain("Rabies");
        text.Should().Contain("Uygulandı");
    }
}
