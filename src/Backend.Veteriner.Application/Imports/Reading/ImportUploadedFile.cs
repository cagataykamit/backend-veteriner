using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Yüklenen tek dosya (bellekte). Ham dosya hiçbir yerde saklanmaz.</summary>
public sealed record ImportUploadedFile(string FileName, string? ContentType, byte[] Content);

/// <summary>
/// <c>POST /imports/batches</c> dosya alanları: ya <see cref="Workbook"/> (XLSX) ya da tablo başına CSV. E-vet profilinde
/// yedek (<see cref="Workbook"/>) ürün listesi raporu (<see cref="ProductsCsv"/>) ve iki yardımcı raporla
/// (<see cref="EVetPatientReport"/>, <see cref="EVetVaccineReport"/>) birlikte gelebilir (IMPORT_API_CONTRACT.md §13).
/// </summary>
public sealed record ImportFileSet(
    ImportUploadedFile? Workbook,
    ImportUploadedFile? CustomersCsv,
    ImportUploadedFile? PetsCsv,
    ImportUploadedFile? VaccinationsCsv,
    ImportUploadedFile? ClinicalRecordsCsv = null,
    ImportUploadedFile? AccountEntriesCsv = null,
    ImportUploadedFile? AppointmentsCsv = null,
    ImportUploadedFile? ProductsCsv = null,
    ImportUploadedFile? EVetPatientReport = null,
    ImportUploadedFile? EVetVaccineReport = null)
{
    public bool HasAny => Workbook is not null || HasCsv;
    public bool HasCsv => CsvFiles.Any();
    public bool HasEVetReports => EVetPatientReport is not null || EVetVaccineReport is not null;

    /// <summary>E-vet yardımcı raporları (CSV; tabloya dönüşmez, yedeğin bağlantısını ve seri numarasını tamamlar).</summary>
    public IEnumerable<(string Key, ImportUploadedFile File)> EVetReports
    {
        get
        {
            if (EVetPatientReport is not null)
                yield return ("evetPatientReport", EVetPatientReport);
            if (EVetVaccineReport is not null)
                yield return ("evetVaccineReport", EVetVaccineReport);
        }
    }

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
