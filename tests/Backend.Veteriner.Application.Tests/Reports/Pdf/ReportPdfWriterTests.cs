using System.Text;
using Backend.Veteriner.Application.Reports;
using Backend.Veteriner.Application.Reports.Pdf;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Pdf;

public sealed class ReportPdfWriterTests
{
    private sealed record Row(string Name, DateTime AtUtc, decimal Amount, decimal? Count);

    private static readonly IReadOnlyList<RegisterColumn<Row>> Columns =
    [
        RegisterColumn<Row>.Text(null, "Müşteri", r => r.Name),
        RegisterColumn<Row>.Time("Tahsilat", "Zaman", r => r.AtUtc),
        RegisterColumn<Row>.Money("Tahsilat", "Tutar", r => r.Amount),
        RegisterColumn<Row>.Number(null, "Adet", r => r.Count),
    ];

    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Write_Should_Produce_Pdf_With_Embedded_Font_For_Turkish_Text()
    {
        Assert.SkipUnless(ReportPdfFonts.IsAvailable, "Sunucuda Türkçe destekli yazı tipi yok (Arial / DejaVu Sans / Liberation Sans).");
        var rows = Enumerable.Range(1, 120)
            .Select(i => new Row($"Şükrü Çağlar Gündoğdu İnce {i}", Now.AddMinutes(-i), 1250.5m + i, i % 3 == 0 ? null : i))
            .ToList();

        var bytes = ReportPdfWriter.Write(new ReportPdfHeader("Ödeme Raporu", "01.10.2026 – 31.10.2026", Now), Columns, rows);

        var text = Encoding.ASCII.GetString(bytes);
        text.Should().StartWith("%PDF-");
        text.Should().Contain("/FontFile2", "Türkçe harfler için yazı tipi gömülmelidir");
        System.Text.RegularExpressions.Regex.Count(text, @"/Type\s*/Page(?!s)").Should().BeGreaterThan(1, "120 satır birden çok sayfaya yayılır");
    }

    [Fact]
    public void Write_Should_Handle_Empty_Rows()
    {
        Assert.SkipUnless(ReportPdfFonts.IsAvailable, "Sunucuda Türkçe destekli yazı tipi yok.");

        var bytes = ReportPdfWriter.Write(new ReportPdfHeader("Borç Raporu", null, Now), Columns, []);

        Encoding.ASCII.GetString(bytes.AsSpan(0, 5)).Should().Be("%PDF-");
    }

    [Fact]
    public void BuildFile_Should_Write_Pdf_With_Spec_Title_And_Pdf_Extension()
    {
        Assert.SkipUnless(ReportPdfFonts.IsAvailable, "Sunucuda Türkçe destekli yazı tipi yok.");
        var spec = new RegisterExportSpec<Row>(Columns, "Odemeler", "odeme-defteri", "Ödeme Defteri");

        var file = RegisterReportRunner.BuildFile(
            spec, [new Row("Ayşe", Now, 10m, 1m)], RegisterExportFormat.Pdf, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30), Now);

        file.FileDownloadName.Should().Be("odeme-defteri-20261030.pdf");
        Encoding.ASCII.GetString(file.Content.AsSpan(0, 5)).Should().Be("%PDF-");
    }

    [Theory]
    [InlineData(2026, 10, 1, 2026, 10, 31, "01.10.2026 – 31.10.2026")]
    [InlineData(2026, 10, 30, 2026, 10, 30, "30.10.2026")]
    public void PeriodText_Should_Format_Istanbul_Days(int y1, int m1, int d1, int y2, int m2, int d2, string expected)
        => ReportPdfHeader.PeriodText(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2)).Should().Be(expected);

    [Fact]
    public void PeriodText_From_Utc_Range_Should_Use_Istanbul_Calendar_Days()
    {
        // İstanbul 1 Ekim 00:00 = 30 Eylül 21:00 UTC; 31 Ekim 23:59:59 = 31 Ekim 20:59:59 UTC.
        var fromUtc = new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 10, 31, 20, 59, 59, DateTimeKind.Utc);

        ReportPdfHeader.PeriodText(fromUtc, toUtc).Should().Be("01.10.2026 – 31.10.2026");
    }

    [Fact]
    public void ExportRowLimit_ForFormat_Should_Cap_Pdf_And_Keep_Trial_Code()
    {
        ExportRowLimit.Paid.ForFormat(RegisterExportFormat.Pdf).Should().Be(new ExportRowLimit(ReportsSharedLimits.MaxPdfExportRows, IsTrial: false));
        ExportRowLimit.Paid.ForFormat(RegisterExportFormat.Csv).Should().Be(ExportRowLimit.Paid);
        ExportRowLimit.Paid.ForFormat(RegisterExportFormat.Xlsx).Should().Be(ExportRowLimit.Paid);
        ExportRowLimit.Trial.ForFormat(RegisterExportFormat.Pdf).Should().Be(ExportRowLimit.Trial);

        var paidPdf = ExportRowLimit.Paid.ForFormat(RegisterExportFormat.Pdf).Ensure(ReportsSharedLimits.MaxPdfExportRows + 1, "Payments.ReportExportTooManyRows");
        var trialPdf = ExportRowLimit.Trial.ForFormat(RegisterExportFormat.Pdf).Ensure(TrialLimits.ExportMaxRows + 1, "Payments.ReportExportTooManyRows");

        paidPdf.Error.Code.Should().Be("Payments.ReportExportTooManyRows");
        paidPdf.Error.Message.Should().Contain("5000");
        trialPdf.Error.Code.Should().Be(TrialLimits.ExportLimitExceededCode);
    }
}
