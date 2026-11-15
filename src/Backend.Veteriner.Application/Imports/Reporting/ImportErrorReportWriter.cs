using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Csv;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Imports.Reporting;

/// <summary>Hata raporu satırı: orijinal kaynak satır ve hata önemindeki sorunlar.</summary>
public sealed record ImportErrorReportRow(
    ImportTable Table,
    int RowNumber,
    IReadOnlyDictionary<string, string?> Source,
    IReadOnlyList<ImportIssue> Errors);

/// <summary>Rapor türü (§7.8): hatalı satırlar ya da bilerek atlanacak satırlar (<c>rows=skipped</c>); başlık ve metinler.</summary>
public sealed record ImportReportKind(string Status, string CodeHeader, string MessageHeader, string EmptySheet, string EmptyMessage)
{
    public static readonly ImportReportKind Errors = new("Hata", "HataKodu", "Aciklama", ImportErrorReportWriter.NoErrorsSheetName, "Hatalı satır yok.");
    public static readonly ImportReportKind Skipped = new("Atlanacak", "NedenKodu", "Neden", "Atlanansiz", "Atlanacak satır yok.");
}

/// <summary>
/// Hata raporu dosyası (IMPORT_API_CONTRACT.md §7.8): orijinal satır + <c>SatirNo, Durum, HataKodu, Aciklama</c>.
/// Dışarı yazılan <b>her</b> metin hücresi <see cref="ImportCellSafety.ForExport"/>'tan geçer (formül enjeksiyonu).
/// </summary>
public static class ImportErrorReportWriter
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvContentType = "text/csv; charset=utf-8";
    public const string XlsxFileName = "vetinity-iceaktar-hatalar.xlsx";
    public const string CsvFileName = "vetinity-iceaktar-hatalar.csv";
    public const string NoErrorsSheetName = "Hatasiz";

    public const string SkippedXlsxFileName = "vetinity-iceaktar-atlananlar.xlsx";
    public const string SkippedCsvFileName = "vetinity-iceaktar-atlananlar.csv";

    public static byte[] BuildXlsx(IReadOnlyList<ImportErrorReportRow> rows, ImportReportKind? kind = null)
    {
        kind ??= ImportReportKind.Errors;
        using var workbook = new XLWorkbook();

        if (rows.Count == 0)
        {
            var empty = workbook.Worksheets.Add(kind.EmptySheet);
            WriteHeader(empty, ["Mesaj"]);
            empty.Cell(2, 1).Value = kind.EmptyMessage;
        }

        foreach (var group in rows.GroupBy(r => r.Table).OrderBy(g => g.Key))
        {
            var sheet = workbook.Worksheets.Add(ImportCanonicalSchema.SheetName(group.Key));
            var sourceHeaders = group
                .SelectMany(r => r.Source.Keys)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            WriteHeader(sheet, [.. sourceHeaders, "SatirNo", "Durum", kind.CodeHeader, kind.MessageHeader]);

            var line = 2;
            foreach (var row in group)
            {
                for (var c = 0; c < sourceHeaders.Count; c++)
                {
                    if (row.Source.TryGetValue(sourceHeaders[c], out var value) && value is not null)
                        sheet.Cell(line, c + 1).Value = ImportCellSafety.ForExport(value);
                }

                var extra = sourceHeaders.Count;
                sheet.Cell(line, extra + 1).Value = row.RowNumber;
                sheet.Cell(line, extra + 2).Value = kind.Status;
                sheet.Cell(line, extra + 3).Value = ImportCellSafety.ForExport(Codes(row));
                sheet.Cell(line, extra + 4).Value = ImportCellSafety.ForExport(Messages(row));
                line++;
            }

            sheet.SheetView.FreezeRows(1);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>UTF-8 BOM'lu, <c>;</c> ayırıcılı; tablolar farklı başlık taşıdığından sabit sütunlar (Kaynak = başlık=değer çiftleri).</summary>
    public static byte[] BuildCsv(IReadOnlyList<ImportErrorReportRow> rows, ImportReportKind? kind = null)
    {
        kind ??= ImportReportKind.Errors;
        var sb = new StringBuilder();
        sb.AppendCsvLine(string.Join(';', new[] { "Tablo", "SatirNo", "Durum", kind.CodeHeader, kind.MessageHeader, "Kaynak" }));

        foreach (var row in rows.OrderBy(r => r.Table).ThenBy(r => r.RowNumber))
        {
            var source = string.Join('|', row.Source.Select(kv => $"{kv.Key}={kv.Value}"));
            string[] cells =
            [
                ImportCanonicalNames.Display(row.Table),
                row.RowNumber.ToString(CultureInfo.InvariantCulture),
                kind.Status,
                Codes(row),
                Messages(row),
                source,
            ];
            sb.AppendCsvLine(string.Join(';', cells.Select(c => CsvCell(ImportCellSafety.ForExport(c)))));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Codes(ImportErrorReportRow row) => string.Join("; ", row.Errors.Select(e => e.Code));

    private static string Messages(ImportErrorReportRow row) => string.Join("; ", row.Errors.Select(e => e.Message));

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = ImportCellSafety.ForExport(headers[c]);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
    }

    private static string CsvCell(string value)
        => value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
