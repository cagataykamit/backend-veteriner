using System.Globalization;
using System.IO.Compression;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>
/// XLSX okuyucu. Dosya ClosedXML'e verilmeden önce zip yapısı denetlenir (giriş sayısı, gerçek açılmış boyut,
/// makro, DOCTYPE/ENTITY). Formül çalıştırılmaz: formül hücresinde yalnız dosyadaki önbellek değeri okunur; metin
/// hücresi <c>= + - @</c> ile başlasa da olduğu gibi (metin olarak) saklanır.
/// </summary>
public static class XlsxTableReader
{
    private const int XmlProbeBytes = 64 * 1024;

    public static Result<ImportReadResult> Read(byte[] content, ImportFileLimits limits)
    {
        var scan = ScanArchive(content, limits);
        if (!scan.IsSuccess)
            return Result<ImportReadResult>.Failure(scan.Error);

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var workbook = new XLWorkbook(stream, new LoadOptions { RecalculateAllFormulas = false });
            return ReadWorkbook(workbook, limits);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Bozuk/kırpılmış paket, geçersiz XML, desteklenmeyen parça: ayrıntı kullanıcıya sızdırılmaz.
            return Failure("Imports.UnsupportedFile", "XLSX dosyası okunamadı; dosya bozuk veya desteklenmeyen biçimde.");
        }
    }

    private static Result<ImportReadResult> ReadWorkbook(XLWorkbook workbook, ImportFileLimits limits)
    {
        if (workbook.Worksheets.Count > limits.MaxSheets)
            return Failure("Imports.UnsupportedFile", $"Çalışma kitabında en fazla {limits.MaxSheets} sayfa olabilir.");

        var tables = new List<ImportSourceTable>();
        var warnings = new List<ImportReadWarning>();
        var seen = new HashSet<ImportTable>();
        var total = 0;

        foreach (var sheet in workbook.Worksheets)
        {
            if (!ImportCanonicalSchema.TryResolveSheet(sheet.Name, out var table))
                continue;

            if (!seen.Add(table))
                return Failure("Imports.UnsupportedFile", $"{ImportCanonicalNames.Display(table)} tablosu için birden fazla sayfa var.");

            var assembled = ImportTableAssembler.Assemble(table, ReadRows(sheet), limits);
            if (!assembled.IsSuccess)
                return Result<ImportReadResult>.Failure(assembled.Error);

            if (assembled.Value is not { Rows.Count: > 0 } source)
            {
                warnings.Add(new ImportReadWarning(
                    "Imports.TableEmpty",
                    table,
                    $"{ImportCanonicalNames.Display(table)} sayfasında veri satırı yok; atlandı."));
                continue;
            }

            total += source.Rows.Count;
            if (total > limits.MaxTotalRows)
                return Failure("Imports.TooManyRows", $"Parti toplamı en fazla {limits.MaxTotalRows} veri satırı olabilir.");

            tables.Add(source);
        }

        if (seen.Count == 0)
        {
            return Failure(
                "Imports.UnsupportedFile",
                "Tanınan sayfa yok; sayfa adları Musteriler, Hayvanlar, Asilar olmalıdır (şablonu indirebilirsiniz).");
        }

        if (tables.Count == 0)
            return Failure("Imports.EmptyFile", "Dosyada içe aktarılacak veri satırı yok.");

        return Result<ImportReadResult>.Success(new ImportReadResult(tables, warnings));
    }

    private static IEnumerable<ImportRawRow> ReadRows(IXLWorksheet sheet)
    {
        // RowsUsed yalnız hücresi olan satırları gezer; milyonlarca boş satır taranmaz.
        foreach (var row in sheet.RowsUsed())
        {
            var cells = new List<string?>();
            var lastColumn = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            for (var c = 1; c <= lastColumn; c++)
                cells.Add(ReadCell(row.Cell(c)));

            yield return new ImportRawRow(row.RowNumber(), cells);
        }
    }

    private static string? ReadCell(IXLCell cell)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        string? text = value.Type switch
        {
            XLDataType.Text => value.GetText(),
            XLDataType.Number => FormatNumber(value.GetNumber()),
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.DateTime => FormatDate(value.GetDateTime()),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string FormatNumber(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
            return string.Empty;

        // decimal bilimsel gösterim üretmez; 15 anlamlı basamağa kadar tam sayı (çip, telefon) bozulmaz.
        if (Math.Abs(number) < 7.9e27)
            return ((decimal)number).ToString(CultureInfo.InvariantCulture);

        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTime date)
        => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Zip yapısı denetimi. Açılmış boyut zip başlığındaki değere değil, akıştan <b>gerçekten okunan</b> bayta göre
    /// sayılır (başlık yalan söyleyebilir); tavan aşılınca okuma durur.
    /// </summary>
    private static Result ScanArchive(byte[] content, ImportFileLimits limits)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            if (zip.Entries.Count > limits.MaxZipEntries)
                return Result.Failure("Imports.UnsupportedFile", "XLSX paketinde çok fazla parça var.");

            var names = zip.Entries.Select(e => e.FullName.ToLowerInvariant()).ToHashSet();
            if (!names.Contains("[content_types].xml") || !names.Contains("xl/workbook.xml"))
                return Result.Failure("Imports.UnsupportedFile", "Dosya geçerli bir XLSX çalışma kitabı değil.");

            if (names.Any(n => n.EndsWith("vbaproject.bin", StringComparison.Ordinal)))
                return Result.Failure("Imports.UnsupportedFile", "Makro içeren çalışma kitapları (.xlsm) desteklenmez.");

            long total = 0;
            var buffer = new byte[81_920];
            foreach (var entry in zip.Entries)
            {
                var isXml = entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                            || entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
                var probe = new MemoryStream();

                using var entryStream = entry.Open();
                int read;
                while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > limits.MaxUncompressedBytes)
                        return Result.Failure("Imports.FileTooLarge", "XLSX paketinin açılmış boyutu izin verilen sınırı aşıyor.");

                    if (isXml && probe.Length < XmlProbeBytes)
                        probe.Write(buffer, 0, (int)Math.Min(read, XmlProbeBytes - probe.Length));
                }

                if (isXml && HasDtd(probe.GetBuffer().AsSpan(0, (int)probe.Length)))
                    return Result.Failure("Imports.UnsupportedFile", "Dosya DTD/varlık bildirimi içeriyor; desteklenmez.");
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return Result.Failure("Imports.UnsupportedFile", "XLSX dosyası okunamadı; dosya bozuk veya desteklenmeyen biçimde.");
        }
    }

    /// <summary>
    /// XML bildiriminden sonra kök öğeden önce gelen DOCTYPE/ENTITY (XXE, milyar gülme). UTF-16 XML bu pakette
    /// beklenmez; BOM'lu UTF-16 parça da reddedilir (ASCII taraması onu göremezdi).
    /// </summary>
    private static bool HasDtd(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
            return true;

        var text = System.Text.Encoding.UTF8.GetString(head);
        return text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
               || text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase);
    }

    private static Result<ImportReadResult> Failure(string code, string message)
        => Result<ImportReadResult>.Failure(code, message);
}
