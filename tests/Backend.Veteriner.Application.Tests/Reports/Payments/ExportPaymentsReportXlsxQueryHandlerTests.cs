using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

public sealed class ExportPaymentsReportXlsxQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportPaymentsReportXlsxQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _payments.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var q = new ExportPaymentsReportXlsxQuery(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, null, null, null, null);
        var r = await CreateHandler().Handle(q, CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateRangeInvalid()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var q = new ExportPaymentsReportXlsxQuery(
            DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), null, null, null, null, null);
        var r = await CreateHandler().Handle(q, CancellationToken.None);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ReportDateRangeInvalid");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ExportTooManyRows()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "A", "B"));
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentsReportConstants.MaxExportRows + 1);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var r = await CreateHandler().Handle(new ExportPaymentsReportXlsxQuery(from, to, null, null, null, null, null), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ReportExportTooManyRows");
    }

    [Fact]
    public async Task Handle_Should_ReturnXlsx_WithHeadersSheetNameAndFormattedCells()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K1", "Ist"));
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var paidUtc = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);
        var pay = new Payment(tid, clinicId, clientId, null, null, null, 99.5m, "TRY", PaymentMethod.Transfer, paidUtc, null);
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { pay });

        _clients
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Clients.Client>());
        _pets
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Pets.Specs.PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Pets.Pet>());
        _clinics
            .Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var result = await CreateHandler().Handle(
            new ExportPaymentsReportXlsxQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileDownloadName.Should().EndWith(".xlsx").And.Contain("tahsilat-raporu");

        using var wb = new XLWorkbook(new MemoryStream(result.Value.Content));
        wb.Worksheets.Should().Contain(ws => ws.Name == "Odemeler");
        var ws = wb.Worksheet("Odemeler");
        ws.Cell(1, 1).GetString().Should().Be("Tarih");
        ws.Cell(1, 8).GetString().Should().Be("Not");
        ws.Cell(2, 7).GetString().Should().Be("Havale-EFT");
        ws.Cell(2, 5).GetDouble().Should().Be(99.5);
        ws.Cell(2, 8).GetString().Should().BeEmpty();
        ws.Cell(2, 4).GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_NotLeakGuids_InXlsxStrings()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var pay = new Payment(tid, clinicId, clientId, null, null, null, 1m, "TRY", PaymentMethod.Cash, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), "x");
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { pay });
        _clients
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Clients.Client>());
        _pets
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Pets.Specs.PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Pets.Pet>());
        _clinics
            .Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var r = await CreateHandler().Handle(
            new ExportPaymentsReportXlsxQuery(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                null, null, null, null, null),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var s = System.Text.Encoding.UTF8.GetString(r.Value!.Content);
        s.Should().NotContain(clientId.ToString("D"));
        s.Should().NotContain(clinicId.ToString("D"));
    }
}
