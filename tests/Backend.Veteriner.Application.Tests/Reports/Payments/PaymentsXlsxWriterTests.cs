using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using ClosedXML.Excel;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

public sealed class PaymentsXlsxWriterTests
{
    [Fact]
    public void WriteClinicReceiptWorkbook_Should_WriteHeadersAndEmptyDataRows()
    {
        var rows = Array.Empty<PaymentReportItemDto>();
        var bytes = PaymentsXlsxWriter.WriteClinicReceiptWorkbook(rows);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Odemeler");
        ws.Cell(1, 1).GetString().Should().Be("Tarih");
        ws.Cell(1, 5).GetString().Should().Be("Tutar");
        ws.LastRowUsed()!.RowNumber().Should().Be(1);
    }
}
