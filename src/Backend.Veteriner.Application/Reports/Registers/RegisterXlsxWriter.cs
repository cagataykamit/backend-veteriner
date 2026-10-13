using Backend.Veteriner.Application.Common.Time;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>
/// Defter XLSX üretimi: gruplu sütunlarda iki katmanlı başlık (aynı gruptaki bitişik sütunlar birleştirilir),
/// biçimli tarih/sayı hücreleri, sabit başlık, filtre, sütun genişliği üst sınırı. Sütunlar yalnız
/// <see cref="RegisterColumn{TRow}"/> listesinden gelir.
/// </summary>
internal static class RegisterXlsxWriter
{
    private const double MaxColumnWidth = 55;

    public static byte[] WriteWorkbook<TRow>(string sheetName, IReadOnlyList<RegisterColumn<TRow>> columns, IEnumerable<TRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);

        var headerRows = WriteHeader(ws, columns);

        var rowIdx = headerRows + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Count; c++)
                WriteCell(ws.Cell(rowIdx, c + 1), columns[c], row);
            rowIdx++;
        }

        var lastRow = Math.Max(rowIdx - 1, headerRows);
        if (rowIdx > headerRows + 1)
            ws.Range(headerRows, 1, lastRow, columns.Count).SetAutoFilter();

        ws.SheetView.FreezeRows(headerRows);

        for (var col = 1; col <= columns.Count; col++)
        {
            ws.Column(col).AdjustToContents(headerRows, lastRow);
            if (ws.Column(col).Width > MaxColumnWidth)
                ws.Column(col).Width = MaxColumnWidth;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Başlık satırı sayısını döner (1 veya gruplu ise 2).</summary>
    private static int WriteHeader<TRow>(IXLWorksheet ws, IReadOnlyList<RegisterColumn<TRow>> columns)
    {
        var grouped = columns.Any(c => c.Group is not null);
        var headerRows = grouped ? 2 : 1;

        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            var detail = ws.Cell(headerRows, c + 1);
            detail.Value = column.Header;
            detail.Style.Font.Bold = true;

            if (!grouped)
                continue;

            if (column.Group is null)
            {
                // Grupsuz sütun iki başlık satırını kaplar.
                ws.Range(1, c + 1, 2, c + 1).Merge();
                ws.Cell(1, c + 1).Value = column.Header;
                ws.Cell(1, c + 1).Style.Font.Bold = true;
            }
            else if (c == 0 || columns[c - 1].Group != column.Group)
            {
                var span = 1;
                while (c + span < columns.Count && columns[c + span].Group == column.Group)
                    span++;

                var top = ws.Range(1, c + 1, 1, c + span);
                top.Merge();
                top.Value = column.Group;
                top.Style.Font.Bold = true;
                top.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        return headerRows;
    }

    private static void WriteCell<TRow>(IXLCell cell, RegisterColumn<TRow> column, TRow row)
    {
        switch (column.Value(row))
        {
            case null:
                break;
            case DateTime utc:
                // Excel hücresi: yerel duvar saati (timezone bilgisi hücrede yok)
                cell.Value = OperationDayBounds.ToLocalDate(utc).ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd.MM.yyyy";
                break;
            case DateOnly day:
                cell.Value = day.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd.MM.yyyy";
                break;
            case decimal number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = "#,##0.###";
                break;
            case var other:
                cell.Value = other.ToString();
                break;
        }
    }
}
