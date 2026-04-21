using System.Text;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

public sealed class ExportPaymentsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ExportPaymentsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _payments.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var q = new ExportPaymentsReportQuery(
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null);

        var r = await CreateHandler().Handle(q, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateRangeInvalid()
    {
        var tid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var q = new ExportPaymentsReportQuery(
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-1),
            null,
            null,
            null,
            null,
            null);

        var r = await CreateHandler().Handle(q, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ReportDateRangeInvalid");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ExportTooManyRows()
    {
        var tid = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var clinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "A", "B"));
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentsReportConstants.MaxExportRows + 1);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var q = new ExportPaymentsReportQuery(from, to, null, null, null, null, null);

        var r = await CreateHandler().Handle(q, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ReportExportTooManyRows");
        _payments.Verify(
            x => x.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnClinicReceiptCsv_WithTurkishHeaders_IstanbulTime_AndSemicolonDelimiter()
    {
        var tid = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var clinicId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var clientId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var paidUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc);
        var pay = new Payment(tid, clinicId, clientId, null, null, null, 10m, "TRY", PaymentMethod.Cash, paidUtc, "a;\"b\"");
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

        var q = new ExportPaymentsReportQuery(paidUtc, to, null, PaymentMethod.Cash, null, null, null);
        var r = await CreateHandler().Handle(q, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom);
        text.Should().StartWith('\uFEFF'.ToString());
        text.Should().Contain("Tarih;Klinik;Müşteri;Hayvan;Tutar;Para Birimi;Ödeme Yöntemi;Not");
        text.Should().NotContain(pay.Id.ToString("D"), "teknik paymentId exportta olmamalı");
        text.Should().Contain("01.04.2026 15:00");
        text.Should().Contain("10;TRY;Nakit;");
        text.Should().Contain("\"a;\"\"b\"\"\"");
        r.Value.FileDownloadName.Should().StartWith("tahsilat-raporu-");
    }

    [Fact]
    public async Task Handle_Should_OmitTechnicalGuids_AndLeaveNullNotesClean()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "Cl", "Ct"));
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var paid = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        var pay = new Payment(tid, clinicId, clientId, null, null, null, 1m, "TRY", PaymentMethod.Card, paid, null);
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

        var q = new ExportPaymentsReportQuery(paid, paid, null, null, null, null, null);
        var r = await CreateHandler().Handle(q, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var text = Encoding.UTF8.GetString(r.Value!.ContentUtf8Bom).TrimStart('\uFEFF');
        var rows = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        rows.Should().HaveCount(2);
        var data = rows[1];
        data.Should().NotContain(clinicId.ToString("D"));
        data.Should().NotContain(clientId.ToString("D"));
        data.Should().Contain("01.05.2026 11:00");
        data.Should().Contain(";1;TRY;Kart;");
        data.Should().EndWith(";Kart;");
    }

    [Fact]
    public async Task Handle_Should_Fail_OnInvalidRange_Before_Count_ForExportIsolation()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var q = new ExportPaymentsReportQuery(
            DateTime.UtcNow.AddDays(-400),
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null);

        var r = await CreateHandler().Handle(q, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ReportRangeTooLong");
        _payments.Verify(x => x.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
