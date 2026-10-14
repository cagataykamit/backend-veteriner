using System.IO.Compression;
using System.Text;
using Backend.Veteriner.Application.Imports.Reading;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>Sentetik içe aktarma test dosyaları (gerçek müşteri verisi yok).</summary>
internal static class ImportTestFiles
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static ImportUploadReader Reader(Action<ImportFileLimits>? configure = null)
        => new(Options.Create(Limits(configure)));

    public static ImportFileLimits Limits(Action<ImportFileLimits>? configure = null)
    {
        var limits = new ImportFileLimits();
        configure?.Invoke(limits);
        return limits;
    }

    /// <summary>Verilen sayfa adı → satırlar (ilk satır başlık) ile XLSX üretir.</summary>
    public static byte[] Xlsx(params (string Sheet, object?[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var sheet = workbook.Worksheets.Add(name);
            for (var r = 0; r < rows.Length; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                    SetCell(sheet.Cell(r + 1, c + 1), rows[r][c]);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: break;
            case string s: cell.Value = s; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case double d: cell.Value = d; break;
            case DateTime dt: cell.Value = dt; break;
            case bool b: cell.Value = b; break;
            default: throw new NotSupportedException(value.GetType().Name);
        }
    }

    public static ImportUploadedFile XlsxFile(byte[] content, string name = "veri.xlsx", string? type = XlsxType)
        => new(name, type, content);

    public static ImportUploadedFile Csv(string text, string name = "veri.csv", Encoding? encoding = null, bool bom = false)
    {
        var enc = encoding ?? new UTF8Encoding(false);
        var bytes = enc.GetBytes(text);
        if (bom)
            bytes = [.. Encoding.UTF8.GetPreamble(), .. bytes];
        return new ImportUploadedFile(name, "text/csv", bytes);
    }

    /// <summary>Var olan XLSX paketine ham parça ekler/değiştirir (saldırı örnekleri için).</summary>
    public static byte[] WithEntry(byte[] xlsx, string entryName, byte[] content)
    {
        using var input = new MemoryStream(xlsx);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
        using (var result = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName != entryName))
            {
                var copy = result.CreateEntry(entry.FullName);
                using var from = entry.Open();
                using var to = copy.Open();
                from.CopyTo(to);
            }

            var added = result.CreateEntry(entryName);
            using var stream = added.Open();
            stream.Write(content);
        }

        return output.ToArray();
    }

    public static byte[] ReadEntry(byte[] xlsx, string entryName)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        using var stream = zip.GetEntry(entryName)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
