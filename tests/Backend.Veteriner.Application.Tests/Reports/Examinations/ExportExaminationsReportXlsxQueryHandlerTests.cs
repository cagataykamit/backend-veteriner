using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;
using Backend.Veteriner.Application.Reports.Examinations.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Examinations;

public sealed class ExportExaminationsReportXlsxQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportExaminationsReportXlsxQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _examinations.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_ReturnXlsx_WithUserFacingHeaders()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        var ex = new Examination(tid, clinicId, petId, null, from.AddHours(1), "a", "b", null, "n");

        _examinations.Setup(x => x.CountAsync(It.IsAny<ExaminationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _examinations
            .Setup(x => x.ListAsync(It.IsAny<ExaminationsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Examination> { ex });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new ExportExaminationsReportXlsxQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        using var wb = new XLWorkbook(new MemoryStream(r.Value!.Content));
        wb.Worksheets.Should().Contain(ws => ws.Name == "Muayeneler");
        var ws = wb.Worksheet("Muayeneler");
        ws.Cell(1, 1).GetString().Should().Be("Muayene Zamanı");
        ws.Cell(1, 9).GetString().Should().Be("Not");
        ws.Cell(2, 9).GetString().Should().Be("n");
        ws.Cell(2, 1).DataType.Should().Be(XLDataType.DateTime);
    }
}
