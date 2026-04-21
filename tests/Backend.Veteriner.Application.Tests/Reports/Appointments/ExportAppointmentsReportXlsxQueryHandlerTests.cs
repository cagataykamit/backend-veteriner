using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Appointments;

public sealed class ExportAppointmentsReportXlsxQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportAppointmentsReportXlsxQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _appointments.Object, _clients.Object, _pets.Object, _clinics.Object);

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

        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        var ap = new Appointment(tid, clinicId, petId, from.AddHours(1), notes: "n");

        _appointments.Setup(x => x.CountAsync(It.IsAny<AppointmentsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments
            .Setup(x => x.ListAsync(It.IsAny<AppointmentsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { ap });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow>());
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow>());

        var r = await CreateHandler().Handle(
            new ExportAppointmentsReportXlsxQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        using var wb = new XLWorkbook(new MemoryStream(r.Value!.Content));
        wb.Worksheets.Should().Contain(ws => ws.Name == "Randevular");
        var ws = wb.Worksheet("Randevular");
        ws.Cell(1, 1).GetString().Should().Be("Randevu Zamanı");
        ws.Cell(1, 6).GetString().Should().Be("Not");
        ws.Cell(2, 6).GetString().Should().Be("n");
        ws.Cell(2, 1).DataType.Should().Be(XLDataType.DateTime);
    }

    [Fact]
    public async Task Handle_Should_Xlsx_UseIstanbulDate_NotRawUtcColumn()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var scheduledUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        var ap = new Appointment(tid, clinicId, petId, scheduledUtc);

        _appointments.Setup(x => x.CountAsync(It.IsAny<AppointmentsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments
            .Setup(x => x.ListAsync(It.IsAny<AppointmentsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { ap });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow> { new(petId, clientId, "P", Guid.NewGuid(), "S") });
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Müşteri") });
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow> { new(clinicId, "Klin") });

        var r = await CreateHandler().Handle(
            new ExportAppointmentsReportXlsxQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        using var wb = new XLWorkbook(new MemoryStream(r.Value!.Content));
        var ws = wb.Worksheet("Randevular");
        ws.Cell(2, 1).DataType.Should().Be(XLDataType.DateTime);
        ws.Cell(2, 1).GetDateTime().Should().Be(new DateTime(2026, 9, 1, 15, 0, 0));
    }
}
