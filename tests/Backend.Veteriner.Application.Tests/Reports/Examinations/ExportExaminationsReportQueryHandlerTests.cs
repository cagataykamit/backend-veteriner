using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Examinations;
using Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;
using Backend.Veteriner.Application.Reports.Examinations.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Examinations;

public sealed class ExportExaminationsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportExaminationsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _examinations.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_ReturnCsv_WithUserFacingHeaders()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var ex = new Examination(
            tid,
            clinicId,
            petId,
            null,
            from.AddHours(2),
            "Sebep",
            "Bulgu",
            null,
            null);

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
            new ExportExaminationsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom);
        text.Should().StartWith('\uFEFF'.ToString());
        text.Should().Contain("Muayene Zamanı");
        text.Should().Contain("Geliş Nedeni");
        text.Should().Contain("Bağlı Randevu");
        text.Should().Contain("Yok");
        text.Should().NotContain(ex.Id.ToString("D"));
        r.Value.FileDownloadName.Should().StartWith("muayene-raporu-");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ExportTooManyRows()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _examinations.Setup(x => x.CountAsync(It.IsAny<ExaminationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExaminationsReportConstants.MaxExportRows + 1);

        var r = await CreateHandler().Handle(
            new ExportExaminationsReportQuery(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Examinations.ReportExportTooManyRows");
    }

    [Fact]
    public async Task Handle_Should_NotIncludeForeignTenantGuids()
    {
        var tid = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _examinations.Setup(x => x.CountAsync(It.IsAny<ExaminationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var ex = new Examination(
            tid,
            clinicId,
            petId,
            null,
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            "v",
            "f",
            null,
            null);
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
            new ExportExaminationsReportQuery(
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
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
    public async Task Handle_Should_WriteNullAssessmentAndNotesSafely()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        var ex = new Examination(tid, clinicId, petId, null, from.AddHours(1), "vr", "fd", null, null);

        _examinations.Setup(x => x.CountAsync(It.IsAny<ExaminationsReportFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _examinations
            .Setup(x => x.ListAsync(It.IsAny<ExaminationsReportFilteredOrderedForExportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Examination> { ex });
        _pets.Setup(x => x.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(petId, Guid.NewGuid(), "P") });
        _clients.Setup(x => x.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow> { new(clinicId, "C") });

        var r = await CreateHandler().Handle(
            new ExportExaminationsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom);
        text.Should().Contain("fd");
        text.Should().Contain("vr");
    }
}
