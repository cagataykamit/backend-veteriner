using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Reports.Vaccinations;

internal static class VaccinationsXlsxWriter
{
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    private const string SheetName = "Aşılar";
    private const double MaxColumnWidth = 55;

    public static byte[] WriteReportWorkbook(IReadOnlyList<VaccinationReportItemDto> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        var headers = new[]
        {
            "Uygulama Tarihi", "Sonraki Tarih", "Klinik", "Müşteri", "Hayvan", "Aşı", "Durum", "Not"
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
            WriteOptionalLocalDate(ws, rowIdx, 1, r.AppliedAtUtc);
            WriteOptionalLocalDate(ws, rowIdx, 2, r.DueAtUtc);

            ws.Cell(rowIdx, 3).Value = r.ClinicName;
            ws.Cell(rowIdx, 4).Value = r.ClientName;
            ws.Cell(rowIdx, 5).Value = r.PetName;
            ws.Cell(rowIdx, 6).Value = r.VaccineName;
            ws.Cell(rowIdx, 7).Value = VaccinationStatusTurkishDisplay.ToLabel(r.Status);
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

    private static void WriteOptionalLocalDate(IXLWorksheet ws, int rowIdx, int col, DateTime? utc)
    {
        if (utc is null)
        {
            ws.Cell(rowIdx, col).Value = string.Empty;
            return;
        }

        var u = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(u, IstanbulTimeZone);
        var localUnspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        ws.Cell(rowIdx, col).Value = localUnspecified;
        ws.Cell(rowIdx, col).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
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
