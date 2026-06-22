using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using ClosedXML.Excel;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>CQRS-15I: XLSX writer contract — boş liste, başlık kolonları ve temel satır smoke.</summary>
public sealed class PaymentsXlsxWriterTests
{
    private static readonly string[] ExpectedHeaders =
    [
        "Tarih", "Klinik", "Müşteri", "Hayvan", "Tutar", "Para Birimi", "Ödeme Yöntemi", "Not"
    ];

    [Fact]
    public void WriteClinicReceiptWorkbook_Should_WriteHeadersAndEmptyDataRows()
    {
        var bytes = PaymentsXlsxWriter.WriteClinicReceiptWorkbook(Array.Empty<PaymentReportItemDto>());
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Odemeler");
        AssertHeaderRow(ws);
        ws.LastRowUsed()!.RowNumber().Should().Be(1);
    }

    [Fact]
    public void WriteClinicReceiptWorkbook_Should_WriteAllHeaderColumns()
    {
        var bytes = PaymentsXlsxWriter.WriteClinicReceiptWorkbook(Array.Empty<PaymentReportItemDto>());
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Odemeler");

        for (var i = 0; i < ExpectedHeaders.Length; i++)
            ws.Cell(1, i + 1).GetString().Should().Be(ExpectedHeaders[i]);
    }

    [Fact]
    public void WriteClinicReceiptWorkbook_Should_WriteSingleDataRow_WithFormattedCells()
    {
        var paidUtc = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);
        var row = new PaymentReportItemDto(
            Guid.NewGuid(),
            paidUtc,
            Guid.NewGuid(),
            "Klinik Adı",
            Guid.NewGuid(),
            "Müşteri Adı",
            null,
            string.Empty,
            99.5m,
            "TRY",
            PaymentMethod.Transfer,
            "Not metni");

        var bytes = PaymentsXlsxWriter.WriteClinicReceiptWorkbook([row]);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Odemeler");

        ws.Cell(2, 2).GetString().Should().Be("Klinik Adı");
        ws.Cell(2, 3).GetString().Should().Be("Müşteri Adı");
        ws.Cell(2, 5).GetDouble().Should().Be(99.5);
        ws.Cell(2, 6).GetString().Should().Be("TRY");
        ws.Cell(2, 7).GetString().Should().Be("Havale-EFT");
        ws.Cell(2, 8).GetString().Should().Be("Not metni");
        ws.AutoFilter.IsEnabled.Should().BeTrue();
    }

    private static void AssertHeaderRow(IXLWorksheet ws)
    {
        ws.Cell(1, 1).GetString().Should().Be("Tarih");
        ws.Cell(1, 5).GetString().Should().Be("Tutar");
        ws.Cell(1, 8).GetString().Should().Be("Not");
    }
}
