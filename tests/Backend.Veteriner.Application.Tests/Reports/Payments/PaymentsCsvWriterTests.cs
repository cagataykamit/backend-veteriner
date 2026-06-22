using System.Text;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>CQRS-15I: CSV writer contract — boş liste, Türkçe karakter ve kaçış davranışı.</summary>
public sealed class PaymentsCsvWriterTests
{
    [Fact]
    public void WriteClinicReceiptReportUtf8Bom_Should_WriteHeadersOnly_WhenRowsEmpty()
    {
        var bytes = PaymentsCsvWriter.WriteClinicReceiptReportUtf8Bom(Array.Empty<PaymentReportItemDto>());
        var text = Encoding.UTF8.GetString(bytes);

        text.Should().StartWith('\uFEFF'.ToString());
        text.TrimStart('\uFEFF').TrimEnd('\r', '\n').Should().Be(
            "Tarih;Klinik;Müşteri;Hayvan;Tutar;Para Birimi;Ödeme Yöntemi;Not");
    }

    [Fact]
    public void WriteClinicReceiptReportUtf8Bom_Should_EscapeSemicolonQuoteAndNewlines()
    {
        var paidUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var row = new PaymentReportItemDto(
            Guid.NewGuid(),
            paidUtc,
            Guid.NewGuid(),
            "Klinik;Adı",
            Guid.NewGuid(),
            "Müşteri \"Özel\"",
            null,
            "Hayvan\nSatır",
            10.5m,
            "TRY",
            PaymentMethod.Cash,
            "Not;virgül\"ve\r\nsatır");

        var text = Encoding.UTF8.GetString(PaymentsCsvWriter.WriteClinicReceiptReportUtf8Bom([row]))
            .TrimStart('\uFEFF');

        text.Should().Contain("\"Klinik;Adı\"");
        text.Should().Contain("\"Müşteri \"\"Özel\"\"\"");
        text.Should().Contain("\"Hayvan\nSatır\"");
        text.Should().Contain("\"Not;virgül\"\"ve\r\nsatır\"");
        text.Should().Contain("10,5;TRY;Nakit;");
    }

    [Fact]
    public void WriteClinicReceiptReportUtf8Bom_Should_UseTurkishHeadersAndMethodLabels()
    {
        var paidUtc = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            CreateRow(paidUtc, PaymentMethod.Card, "Kart satırı"),
            CreateRow(paidUtc, PaymentMethod.Transfer, "Havale satırı"),
            CreateRow(paidUtc, (PaymentMethod)99, "Diğer satırı")
        };

        var text = Encoding.UTF8.GetString(PaymentsCsvWriter.WriteClinicReceiptReportUtf8Bom(rows))
            .TrimStart('\uFEFF');

        text.Should().Contain("Müşteri");
        text.Should().Contain("Ödeme Yöntemi");
        text.Should().Contain(";Kart;");
        text.Should().Contain(";Havale-EFT;");
        text.Should().Contain(";Diğer;");
    }

    private static PaymentReportItemDto CreateRow(DateTime paidUtc, PaymentMethod method, string notes)
        => new(
            Guid.NewGuid(),
            paidUtc,
            Guid.NewGuid(),
            "Klinik",
            Guid.NewGuid(),
            "Müşteri",
            null,
            string.Empty,
            1m,
            "TRY",
            method,
            notes);
}
