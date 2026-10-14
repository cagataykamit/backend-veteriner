using System.Text;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Imports.Template;

/// <summary>
/// İçe aktarma şablonu (IMPORT_API_CONTRACT.md §4.2). Veri sayfaları yalnız kanonik başlık satırını taşır: örnek veri
/// <c>Aciklama</c> sayfasındadır, böylece doldurulmadan yüklenen şablon sahte kayıt üretmez. Başlıklar ve örnekler
/// <see cref="ImportCanonicalSchema"/>'dan gelir (kural tek yerde).
/// </summary>
public static class ImportTemplateBuilder
{
    public const string ExplanationSheetName = "Aciklama";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvContentType = "text/csv; charset=utf-8";

    private const double MaxColumnWidth = 60;

    public static byte[] BuildXlsx()
    {
        using var workbook = new XLWorkbook();

        foreach (var table in ImportCanonicalNames.Tables)
            AddDataSheet(workbook, table);

        AddExplanationSheet(workbook);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>UTF-8 BOM'lu, <c>;</c> ayırıcılı, yalnız başlık satırı (Türkçe Excel doğrudan açar).</summary>
    public static byte[] BuildCsv(ImportTable table)
    {
        var header = string.Join(';', ImportCanonicalSchema.Fields(table).Select(f => f.Name));
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(header + "\r\n");
        return [.. preamble, .. body];
    }

    public static string CsvFileName(ImportTable table) => $"vetinity-iceaktar-{ImportCanonicalSchema.SheetName(table).ToLowerInvariant()}.csv";

    public const string XlsxFileName = "vetinity-iceaktar-sablon.xlsx";

    private static void AddDataSheet(XLWorkbook workbook, ImportTable table)
    {
        var sheet = workbook.Worksheets.Add(ImportCanonicalSchema.SheetName(table));
        var fields = ImportCanonicalSchema.Fields(table);

        for (var i = 0; i < fields.Count; i++)
        {
            var column = i + 1;
            var cell = sheet.Cell(1, column);
            cell.Value = ImportCellSafety.ForExport(fields[i].Name);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Metin biçimi: baştaki 0, 15 haneli çip ve tarihin yerel biçime çevrilmesi korunur.
            if (fields[i].IsText)
                sheet.Column(column).Style.NumberFormat.Format = "@";

            sheet.Column(column).Width = Math.Max(14, fields[i].Name.Length + 4);
        }

        sheet.SheetView.FreezeRows(1);
    }

    private static void AddExplanationSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add(ExplanationSheetName);
        string[] headers = ["Tablo (sayfa)", "Başlık", "Zorunlu", "Açıklama", "Örnek"];
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        var row = 2;
        foreach (var table in ImportCanonicalNames.Tables)
        {
            foreach (var field in ImportCanonicalSchema.Fields(table))
            {
                sheet.Cell(row, 1).Value = ImportCellSafety.ForExport(ImportCanonicalSchema.SheetName(table));
                sheet.Cell(row, 2).Value = ImportCellSafety.ForExport(field.Name);
                sheet.Cell(row, 3).Value = ImportCellSafety.ForExport(field.RequiredNote);
                sheet.Cell(row, 4).Value = ImportCellSafety.ForExport(field.Description);
                sheet.Cell(row, 5).Value = ImportCellSafety.ForExport(field.Example);
                row++;
            }
        }

        sheet.SheetView.FreezeRows(1);
        for (var c = 1; c <= headers.Length; c++)
        {
            sheet.Column(c).AdjustToContents(1, row - 1);
            if (sheet.Column(c).Width > MaxColumnWidth)
                sheet.Column(c).Width = MaxColumnWidth;
        }
    }
}
