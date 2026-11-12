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
    string? Delimiter,
    ImportSourceProfile Profile = ImportSourceProfile.Generic);

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
    private readonly TimeProvider _timeProvider;

    public ImportUploadReader(IOptions<ImportFileLimits> limits, TimeProvider? timeProvider = null)
    {
        _limits = limits.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Result<ImportPreparedUpload> Prepare(
        ImportFileSet files,
        string? encoding,
        string? delimiter,
        ImportSourceProfile profile = ImportSourceProfile.Generic)
    {
        if (!files.HasAny)
            return Fail("Imports.EmptyFile", "Yüklenecek dosya yok; file (XLSX) veya tablo başına CSV alanlarından birini (customersFile, petsFile, vaccinationsFile, clinicalRecordsFile, accountEntriesFile, appointmentsFile, productsFile) gönderin.");

        var combination = CheckCombination(files, profile);
        if (!combination.IsSuccess)
            return Result<ImportPreparedUpload>.Failure(combination.Error);

        foreach (var (file, isXlsx) in Enumerate(files))
        {
            var checkedFile = Inspect(file, isXlsx);
            if (!checkedFile.IsSuccess)
                return Result<ImportPreparedUpload>.Failure(checkedFile.Error);
        }

        return Result<ImportPreparedUpload>.Success(
            new ImportPreparedUpload(files, ComputeHash(files), encoding, delimiter, profile));
    }

    /// <summary>
    /// Ayrıştırma ve tavan denetimi. <paramref name="trialTenant"/> ise tablo satır tavanı deneme sınırıdır
    /// (<see cref="ImportFileLimits.ForTrial"/>, TRIAL-004); kararı <c>ITrialLimitPolicy</c> ile çağıran verir.
    /// E-vet profilinde E-vet yedeği kanonik tablolara çevrilir (<see cref="EVet.EVetBackupConverter"/>).
    /// </summary>
    public Result<ImportReadResult> Read(ImportPreparedUpload upload, bool trialTenant)
    {
        var limits = trialTenant ? _limits.ForTrial() : _limits;
        var files = upload.Files;
        if (files.Workbook is not null)
        {
            if (upload.Profile != ImportSourceProfile.EVet)
                return XlsxTableReader.Read(files.Workbook.Content, limits);

            return XlsxTableReader.Open(files.Workbook.Content, limits, workbook =>
            {
                if (EVet.EVetBackupConverter.IsBackup(workbook))
                    return ReadEVetBackup(workbook, upload, limits);

                return files.HasCsv || files.HasEVetReports
                    ? Fail<ImportReadResult>("Imports.UnsupportedFile", "Excel dosyası E-vet yedeği değil (Genel > Yedek Al); raporlar yalnız yedekle birlikte yüklenir.")
                    : XlsxTableReader.ReadWorkbook(workbook, limits);
            });
        }

        var tables = new List<ImportSourceTable>();
        var warnings = new List<ImportReadWarning>();
        var csv = ReadCsvTables(upload, limits, tables, warnings);
        if (!csv.IsSuccess)
            return Result<ImportReadResult>.Failure(csv.Error);

        if (tables.Count == 0)
            return Fail<ImportReadResult>("Imports.EmptyFile", "Dosyada içe aktarılacak veri satırı yok.");

        return Result<ImportReadResult>.Success(new ImportReadResult(tables, warnings));
    }

    private Result<ImportReadResult> ReadEVetBackup(ClosedXML.Excel.XLWorkbook workbook, ImportPreparedUpload upload, ImportFileLimits limits)
    {
        var reports = EVet.EVetReports.Read(upload.Files.EVetPatientReport, upload.Files.EVetVaccineReport);
        if (!reports.IsSuccess)
            return Result<ImportReadResult>.Failure(reports.Error);

        var converted = EVet.EVetBackupConverter.Convert(workbook, reports.Value!, limits, _timeProvider.GetUtcNow().UtcDateTime);
        if (!converted.IsSuccess)
            return converted;

        var tables = converted.Value!.Tables.ToList();
        var warnings = converted.Value.Warnings.ToList();
        var csv = ReadCsvTables(upload, limits, tables, warnings);
        if (!csv.IsSuccess)
            return Result<ImportReadResult>.Failure(csv.Error);

        return tables.Count == 0
            ? Fail<ImportReadResult>("Imports.EmptyFile", "Dosyada içe aktarılacak veri satırı yok.")
            : Result<ImportReadResult>.Success(new ImportReadResult(tables, warnings));
    }

    /// <summary>
    /// CSV tablolarını okur ve <paramref name="tables"/>'a ekler; parti toplam tavanı eklenmiş tablolarla birlikte sayılır.
    /// E-vet profilinde ürün dosyası E-vet "Ürün Listesi" raporuysa gruplu biçimi çözülür.
    /// </summary>
    private static Result ReadCsvTables(
        ImportPreparedUpload upload,
        ImportFileLimits limits,
        List<ImportSourceTable> tables,
        List<ImportReadWarning> warnings)
    {
        var total = tables.Sum(t => t.Rows.Count);
        foreach (var (table, file) in CsvInputs(upload.Files))
        {
            var read = table == ImportTable.Products && upload.Profile == ImportSourceProfile.EVet
                       && EVet.EVetProductList.TryRead(file.Content, limits, warnings) is { } evetProducts
                ? evetProducts
                : CsvTableReader.Read(table, file.Content, upload.Encoding, upload.Delimiter, limits, warnings);
            if (!read.IsSuccess)
                return Result.Failure(read.Error);

            if (read.Value is not { Rows.Count: > 0 } source)
            {
                warnings.Add(new ImportReadWarning(
                    "Imports.TableEmpty",
                    table,
                    $"{ImportCanonicalNames.Display(table)} dosyasında veri satırı yok; atlandı."));
                continue;
            }

            total += source.Rows.Count;
            if (total > limits.MaxTotalRows)
                return Result.Failure("Imports.TooManyRows", $"Parti toplamı en fazla {limits.MaxTotalRows} veri satırı olabilir.");

            tables.Add(source);
        }

        return Result.Success();
    }

    /// <summary>
    /// Alan birlikteliği: XLSX ile CSV yalnız E-vet profilinde (yedek + ürün listesi raporu) birlikte gelir; E-vet yardımcı
    /// raporları yalnız E-vet profilinde ve yedekle birlikte kabul edilir.
    /// </summary>
    private static Result CheckCombination(ImportFileSet files, ImportSourceProfile profile)
    {
        if (files.HasEVetReports && profile != ImportSourceProfile.EVet)
            return Result.Failure("Imports.UnsupportedFile", "E-vet raporları (evetPatientReportFile, evetVaccineReportFile) yalnız E-vet profiliyle yüklenir.");

        if (files.HasEVetReports && files.Workbook is null)
            return Result.Failure("Imports.UnsupportedFile", "E-vet raporları E-vet yedeğiyle (Genel > Yedek Al, file alanı) birlikte yüklenir.");

        if (files.Workbook is null || !files.HasCsv)
            return Result.Success();

        return profile == ImportSourceProfile.EVet && files.CsvFiles.All(f => f.Table == ImportTable.Products)
            ? Result.Success()
            : Result.Failure("Imports.UnsupportedFile", "file (XLSX) ile CSV alanları birlikte gönderilemez (yalnız E-vet yedeği ürün listesiyle gelebilir).");
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

    /// <summary>
    /// Tek XLSX: dosyanın özeti. Aksi halde <c>anahtar=özet</c> parçalarının (<c>file</c>, tablo anahtarları, E-vet raporları)
    /// <c>;</c> ile birleşiminin özeti.
    /// </summary>
    private static string ComputeHash(ImportFileSet files)
    {
        if (files.Workbook is not null && !files.HasCsv && !files.HasEVetReports)
            return Convert.ToHexStringLower(SHA256.HashData(files.Workbook.Content));

        var inputs = CsvInputs(files).Select(x => (Key: ImportCanonicalNames.Key(x.Table), x.File)).Concat(files.EVetReports);
        if (files.Workbook is not null)
            inputs = inputs.Prepend(("file", files.Workbook));

        var parts = inputs.Select(x => $"{x.Key}={Convert.ToHexStringLower(SHA256.HashData(x.File.Content))}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join(';', parts))));
    }

    private static IEnumerable<(ImportUploadedFile File, bool IsXlsx)> Enumerate(ImportFileSet files)
    {
        if (files.Workbook is not null)
            yield return (files.Workbook, true);

        foreach (var (_, file) in CsvInputs(files))
            yield return (file, false);

        foreach (var (_, file) in files.EVetReports)
            yield return (file, false);
    }

    private static IEnumerable<(ImportTable Table, ImportUploadedFile File)> CsvInputs(ImportFileSet files) => files.CsvFiles;

    private static Result<ImportPreparedUpload> Fail(string code, string message)
        => Fail<ImportPreparedUpload>(code, message);

    private static Result<T> Fail<T>(string code, string message)
        => Result<T>.Failure(code, message);
}
