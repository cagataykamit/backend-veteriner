using Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Reports.Examinations;

internal static class ExaminationsXlsxWriter
{
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    private const string SheetName = "Muayeneler";
    private const double MaxColumnWidth = 55;

    public static byte[] WriteReportWorkbook(IReadOnlyList<ExaminationReportItemDto> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        var headers = new[]
        {
            "Muayene Zamanı", "Klinik", "Müşteri", "Hayvan", "Bağlı Randevu",
            "Geliş Nedeni", "Bulgular", "Değerlendirme", "Not"
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
            var utc = DateTime.SpecifyKind(r.ExaminedAtUtc, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, IstanbulTimeZone);
            var localUnspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            ws.Cell(rowIdx, 1).Value = localUnspecified;
            ws.Cell(rowIdx, 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";

            ws.Cell(rowIdx, 2).Value = r.ClinicName;
            ws.Cell(rowIdx, 3).Value = r.ClientName;
            ws.Cell(rowIdx, 4).Value = r.PetName;
            ws.Cell(rowIdx, 5).Value = r.AppointmentId.HasValue ? "Var" : "Yok";
            ws.Cell(rowIdx, 6).Value = r.VisitReason;
            ws.Cell(rowIdx, 7).Value = r.Findings;
            ws.Cell(rowIdx, 8).Value = r.Assessment ?? string.Empty;
            ws.Cell(rowIdx, 9).Value = r.Notes ?? string.Empty;
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
