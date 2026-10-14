using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Biçim/imza/boyut denetiminden geçmiş yükleme; içerik özeti hesaplanmıştır.</summary>
public sealed record ImportPreparedUpload(
    ImportFileSet Files,
    string ContentHash,
    string? Encoding,
    string? Delimiter);

/// <summary>
/// Yüklemenin iki adımı: <see cref="Prepare"/> (ucuz: uzantı, imza, MIME, boyut, SHA-256) ve <see cref="Read"/>
/// (pahalı: ayrıştırma ve satır/sütun tavanları). Tekrar/mükerrer dosya kararları bu iki adım arasında verilir.
/// </summary>
public sealed class ImportUploadReader
{
    private static readonly string[] XlsxContentTypes =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/octet-stream",
        "application/zip",
        "application/x-zip-compressed",
    ];

    private static readonly string[] CsvContentTypes =
    [
        "text/csv",
        "application/csv",
        "text/plain",
        "text/comma-separated-values",
        "application/vnd.ms-excel",
        "application/octet-stream",
    ];

    private readonly ImportFileLimits _limits;

    public ImportUploadReader(IOptions<ImportFileLimits> limits) => _limits = limits.Value;

    public Result<ImportPreparedUpload> Prepare(ImportFileSet files, string? encoding, string? delimiter)
    {
        if (!files.HasAny)
            return Fail("Imports.EmptyFile", "Yüklenecek dosya yok; file veya customersFile/petsFile/vaccinationsFile gönderin.");

        if (files.Workbook is not null && files.HasCsv)
            return Fail("Imports.UnsupportedFile", "file (XLSX) ile CSV alanları birlikte gönderilemez.");

        foreach (var (file, isXlsx) in Enumerate(files))
        {
            var checkedFile = Inspect(file, isXlsx);
            if (!checkedFile.IsSuccess)
                return Result<ImportPreparedUpload>.Failure(checkedFile.Error);
        }

        return Result<ImportPreparedUpload>.Success(
            new ImportPreparedUpload(files, ComputeHash(files), encoding, delimiter));
    }

    public Result<ImportReadResult> Read(ImportPreparedUpload upload)
    {
        var files = upload.Files;
        if (files.Workbook is not null)
        {
            return XlsxTableReader.Read(files.Workbook.Content, _limits);
        }

        var tables = new List<ImportSourceTable>();
        var warnings = new List<ImportReadWarning>();
        var total = 0;
        foreach (var (table, file) in CsvInputs(files))
        {
            var read = CsvTableReader.Read(table, file.Content, upload.Encoding, upload.Delimiter, _limits, warnings);
            if (!read.IsSuccess)
                return Result<ImportReadResult>.Failure(read.Error);

            if (read.Value is not { Rows.Count: > 0 } source)
            {
                warnings.Add(new ImportReadWarning(
                    "Imports.TableEmpty",
                    table,
                    $"{ImportCanonicalNames.Display(table)} dosyasında veri satırı yok; atlandı."));
                continue;
            }

            total += source.Rows.Count;
            if (total > _limits.MaxTotalRows)
                return Fail<ImportReadResult>("Imports.TooManyRows", $"Parti toplamı en fazla {_limits.MaxTotalRows} veri satırı olabilir.");

            tables.Add(source);
        }

        if (tables.Count == 0)
            return Fail<ImportReadResult>("Imports.EmptyFile", "Dosyada içe aktarılacak veri satırı yok.");

        return Result<ImportReadResult>.Success(new ImportReadResult(tables, warnings));
    }

    private Result Inspect(ImportUploadedFile file, bool expectXlsx)
    {
        if (file.Content.Length == 0)
            return Result.Failure("Imports.EmptyFile", "Dosya boş.");

        if (file.Content.Length > _limits.MaxFileBytes)
        {
            return Result.Failure(
                "Imports.FileTooLarge",
                $"Dosya en fazla {_limits.MaxFileBytes / (1024 * 1024)} MB olabilir.");
        }

        var extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        var expectedExtension = expectXlsx ? ".xlsx" : ".csv";
        if (extension != expectedExtension)
        {
            return Result.Failure(
                "Imports.UnsupportedFile",
                expectXlsx
                    ? "file alanı yalnız .xlsx kabul eder (.xls, .xlsm ve parolalı dosyalar desteklenmez); CSV için tablo alanlarını kullanın."
                    : "CSV alanları yalnız .csv dosyası kabul eder.");
        }

        var contentType = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(contentType)
            && !(expectXlsx ? XlsxContentTypes : CsvContentTypes).Contains(contentType))
        {
            return Result.Failure("Imports.UnsupportedFile", $"Dosya türü kabul edilmiyor ({contentType}).");
        }

        var content = file.Content;
        var isZip = content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;
        var isOle = content.Length >= 4 && content[0] == 0xD0 && content[1] == 0xCF && content[2] == 0x11 && content[3] == 0xE0;

        if (isOle)
        {
            return Result.Failure(
                "Imports.UnsupportedFile",
                "Eski Excel (.xls) veya parolalı dosya desteklenmez; parolasız .xlsx olarak kaydedin.");
        }

        if (expectXlsx)
        {
            return isZip
                ? Result.Success()
                : Result.Failure("Imports.UnsupportedFile", "Dosya içeriği XLSX biçiminde değil.");
        }

        if (isZip || LooksBinary(content))
            return Result.Failure("Imports.UnsupportedFile", "Dosya içeriği CSV (metin) biçiminde değil.");

        return Result.Success();
    }

    /// <summary>Başka bir ikili biçim imzası veya ilk 8 KB'de NUL bayt (UTF-16 BOM'lu metin hariç).</summary>
    private static bool LooksBinary(byte[] content)
    {
        if (StartsWith(content, "%PDF") || StartsWith(content, "MZ") || StartsWith(content, "GIF8")
            || (content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
            || (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            || (content.Length >= 2 && content[0] == 0x1F && content[1] == 0x8B))
        {
            return true;
        }

        var utf16Bom = content.Length >= 2
                       && ((content[0] == 0xFF && content[1] == 0xFE) || (content[0] == 0xFE && content[1] == 0xFF));
        return !utf16Bom && content.AsSpan(0, Math.Min(content.Length, 8192)).Contains((byte)0);
    }

    private static bool StartsWith(byte[] content, string ascii)
    {
        if (content.Length < ascii.Length)
            return false;

        for (var i = 0; i < ascii.Length; i++)
        {
            if (content[i] != ascii[i])
                return false;
        }

        return true;
    }

    private static string ComputeHash(ImportFileSet files)
    {
        if (files.Workbook is not null)
            return Convert.ToHexStringLower(SHA256.HashData(files.Workbook.Content));

        var parts = CsvInputs(files)
            .Select(x => $"{ImportCanonicalNames.Key(x.Table)}={Convert.ToHexStringLower(SHA256.HashData(x.File.Content))}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join(';', parts))));
    }

    private static IEnumerable<(ImportUploadedFile File, bool IsXlsx)> Enumerate(ImportFileSet files)
    {
        if (files.Workbook is not null)
            yield return (files.Workbook, true);

        foreach (var (_, file) in CsvInputs(files))
            yield return (file, false);
    }

    private static IEnumerable<(ImportTable Table, ImportUploadedFile File)> CsvInputs(ImportFileSet files)
    {
        if (files.CustomersCsv is not null)
            yield return (ImportTable.Customers, files.CustomersCsv);
        if (files.PetsCsv is not null)
            yield return (ImportTable.Pets, files.PetsCsv);
        if (files.VaccinationsCsv is not null)
            yield return (ImportTable.Vaccinations, files.VaccinationsCsv);
    }

    private static Result<ImportPreparedUpload> Fail(string code, string message)
        => Fail<ImportPreparedUpload>(code, message);

    private static Result<T> Fail<T>(string code, string message)
        => Result<T>.Failure(code, message);
}
