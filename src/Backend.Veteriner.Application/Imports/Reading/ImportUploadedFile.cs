using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Yüklenen tek dosya (bellekte). Ham dosya hiçbir yerde saklanmaz.</summary>
public sealed record ImportUploadedFile(string FileName, string? ContentType, byte[] Content);

/// <summary>
/// <c>POST /imports/batches</c> dosya alanları: ya <see cref="Workbook"/> (XLSX) ya da tablo başına CSV.
/// </summary>
public sealed record ImportFileSet(
    ImportUploadedFile? Workbook,
    ImportUploadedFile? CustomersCsv,
    ImportUploadedFile? PetsCsv,
    ImportUploadedFile? VaccinationsCsv,
    ImportUploadedFile? ClinicalRecordsCsv = null,
    ImportUploadedFile? AccountEntriesCsv = null,
    ImportUploadedFile? AppointmentsCsv = null,
    ImportUploadedFile? ProductsCsv = null)
{
    public bool HasAny => Workbook is not null || HasCsv;
    public bool HasCsv => CsvFiles.Any();

    /// <summary>Verilen CSV dosyaları, tablo sırasıyla.</summary>
    public IEnumerable<(ImportTable Table, ImportUploadedFile File)> CsvFiles
        => ImportTables.All.Select(t => (Table: t, File: Csv(t))).Where(x => x.File is not null).Select(x => (x.Table, x.File!));

    public ImportUploadedFile? Csv(ImportTable table) => table switch
    {
        ImportTable.Customers => CustomersCsv,
        ImportTable.Pets => PetsCsv,
        ImportTable.Vaccinations => VaccinationsCsv,
        ImportTable.ClinicalRecords => ClinicalRecordsCsv,
        ImportTable.AccountEntries => AccountEntriesCsv,
        ImportTable.Appointments => AppointmentsCsv,
        ImportTable.Products => ProductsCsv,
        _ => null,
    };
}
