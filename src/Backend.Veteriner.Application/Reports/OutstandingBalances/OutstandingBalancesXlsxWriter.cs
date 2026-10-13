using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>Borç raporu XLSX üretimi: biçimli tarih/tutar, sabit başlık, filtre, sütun genişliği üst sınırı.</summary>
internal static class OutstandingBalancesXlsxWriter
{
    private const string SheetName = "Borclar";
    private const double MaxColumnWidth = 55;

    public static byte[] WriteWorkbook(IReadOnlyList<OutstandingBalanceItemDto> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        var headers = OutstandingBalancesReportColumns.Headers;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }

        var rowIdx = 2;
        foreach (var r in rows)
        {
            ws.Cell(rowIdx, 1).Value = r.ClientName;
            ws.Cell(rowIdx, 2).Value = r.Phone ?? string.Empty;

            ws.Cell(rowIdx, 3).Value = r.OutstandingAmount;
            ws.Cell(rowIdx, 3).Style.NumberFormat.Format = "#,##0.00";

            ws.Cell(rowIdx, 4).Value = Charge.DefaultCurrency;
            ws.Cell(rowIdx, 5).Value = r.OpenChargeCount;

            // Excel hücresi: yerel duvar saati (timezone bilgisi hücrede yok)
            ws.Cell(rowIdx, 6).Value = DateTime.SpecifyKind(
                OutstandingBalancesReportTime.ToIstanbul(r.OldestOpenChargeAtUtc).Date, DateTimeKind.Unspecified);
            ws.Cell(rowIdx, 6).Style.DateFormat.Format = "dd.MM.yyyy";

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
}
