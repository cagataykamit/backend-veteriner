using System.Globalization;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Appointments;

public sealed class ExportAppointmentsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportAppointmentsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _appointments.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_ReturnCsv_WithBomAndSemicolon()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var ap = new Appointment(tid, clinicId, petId, from.AddHours(3), notes: null);

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
            new ExportAppointmentsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom);
        text.Should().StartWith('\uFEFF'.ToString());
        text.Should().Contain("Randevu Zamanı");
        text.Should().Contain("Klinik;Müşteri;Hayvan;Durum;Not");
        text.Should().Contain("Planlanmış");
        text.Should().NotContain("appointmentId");
        text.Should().NotContain(petId.ToString("D"));
        r.Value.FileDownloadName.Should().StartWith("randevu-raporu-");
    }

    [Fact]
    public async Task Handle_Should_FormatCsvDate_AsIstanbulLocal()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var scheduledUtc = new DateTime(2026, 7, 1, 3, 36, 0, DateTimeKind.Utc);
        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var ap = new Appointment(tid, clinicId, petId, scheduledUtc);

        _appointments.Setup(x => x.CountAsync(It.IsAny<AppointmentsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments
            .Setup(x => x.ListAsync(It.IsAny<AppointmentsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { ap });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow> { new(petId, clientId, "Dost", speciesId, "Kedi") });
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Fatma Kaya") });
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow> { new(clinicId, "Vet A") });

        var r = await CreateHandler().Handle(
            new ExportAppointmentsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom);
        var tz = ResolveIstanbulTimeZone();
        var expectedLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(scheduledUtc, DateTimeKind.Utc), tz)
            .ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR"));
        text.Should().Contain(expectedLocal);
    }

    [Fact]
    public async Task Handle_Should_KeepNullNotesSafe_InCsv()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var ap = new Appointment(tid, clinicId, petId, from.AddHours(2), notes: null);

        _appointments.Setup(x => x.CountAsync(It.IsAny<AppointmentsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments
            .Setup(x => x.ListAsync(It.IsAny<AppointmentsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { ap });

        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow> { new(petId, clientId, "P", Guid.NewGuid(), "S") });
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "C") });
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow> { new(clinicId, "Cl") });

        var r = await CreateHandler().Handle(
            new ExportAppointmentsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var lines = System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom).Split('\n');
        lines.Length.Should().BeGreaterThanOrEqualTo(2);
        lines[1].TrimEnd('\r').Should().EndWith("Planlanmış;");
    }

    private static TimeZoneInfo ResolveIstanbulTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ExportTooManyRows()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(x => x.CountAsync(It.IsAny<AppointmentsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppointmentsReportConstants.MaxExportRows + 1);

        var r = await CreateHandler().Handle(
            new ExportAppointmentsReportQuery(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Appointments.ReportExportTooManyRows");
    }

    [Fact]
    public async Task Handle_Should_NotIncludeOtherTenantGuids()
    {
        var tid = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(x => x.CountAsync(It.IsAny<AppointmentsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var ap = new Appointment(tid, clinicId, petId, new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc));
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
            new ExportAppointmentsReportQuery(
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
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
}
