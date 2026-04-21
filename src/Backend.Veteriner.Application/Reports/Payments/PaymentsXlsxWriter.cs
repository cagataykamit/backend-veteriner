using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Reports.Payments;

/// <summary>
/// Klinik içi tahsilat raporu XLSX üretimi: biçimli tarih/tutar, sabit başlık, filtre, daraltılmamış sütun genişliği riskine karşı üst sınır.
/// </summary>
internal static class PaymentsXlsxWriter
{
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    private const string SheetName = "Odemeler";
    private const double MaxColumnWidth = 55;

    /// <summary>UTF-8 metinler; Excel TR’de tarih/tutar hücre biçimleri.</summary>
    public static byte[] WriteClinicReceiptWorkbook(IReadOnlyList<PaymentReportItemDto> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        var headers = new[]
        {
            "Tarih", "Klinik", "Müşteri", "Hayvan", "Tutar", "Para Birimi", "Ödeme Yöntemi", "Not"
        };

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }

        var rowIdx = 2;
        foreach (var r in rows)
        {
            var paidUtc = DateTime.SpecifyKind(r.PaidAtUtc, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(paidUtc, IstanbulTimeZone);
            // Excel hücresi: yerel duvar saati (timezone bilgisi hücrede yok)
            var localUnspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

            ws.Cell(rowIdx, 1).Value = localUnspecified;
            ws.Cell(rowIdx, 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";

            ws.Cell(rowIdx, 2).Value = r.ClinicName;
            ws.Cell(rowIdx, 3).Value = r.ClientName;
            ws.Cell(rowIdx, 4).Value = r.PetName ?? string.Empty;

            ws.Cell(rowIdx, 5).Value = r.Amount;
            ws.Cell(rowIdx, 5).Style.NumberFormat.Format = "#,##0.00";

            ws.Cell(rowIdx, 6).Value = r.Currency;
            ws.Cell(rowIdx, 7).Value = PaymentMethodTurkishDisplay.ToLabel(r.Method);
            ws.Cell(rowIdx, 8).Value = r.Notes ?? string.Empty;

            rowIdx++;
        }

        if (rowIdx > 2)
            ws.Range(1, 1, rowIdx - 1, headers.Length).SetAutoFilter();

        ws.SheetView.FreezeRows(1);

        var lastRow = Math.Max(rowIdx - 1, 1);
        for (var col = 1; col <= headers.Length; col++)
        {
            ws.Column(col).AdjustToContents(1, lastRow);
            if (ws.Column(col).Width > MaxColumnWidth)
                ws.Column(col).Width = MaxColumnWidth;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
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
}
