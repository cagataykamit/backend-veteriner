using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Vaccinations.Queries.ExportVaccinationReport;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Vaccinations;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Vaccinations;

public sealed class ExportVaccinationsReportXlsxQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportVaccinationsReportXlsxQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _vaccinations.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_ReturnXlsx_WithSheetAndHeaders()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        var applied = from.AddDays(1);
        var vac = new Vaccination(
            tid,
            petId,
            clinicId,
            null,
            "DHPPi",
            VaccinationStatus.Applied,
            applied,
            null,
            "not");

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
            new ExportVaccinationsReportXlsxQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        using var wb = new XLWorkbook(new MemoryStream(r.Value!.Content));
        wb.Worksheets.Should().Contain(ws => ws.Name == "Aşılar");
        var ws = wb.Worksheet("Aşılar");
        ws.Cell(1, 6).GetString().Should().Be("Aşı");
        ws.Cell(2, 8).GetString().Should().Be("not");
        ws.Cell(2, 1).DataType.Should().Be(XLDataType.DateTime);
    }
}
