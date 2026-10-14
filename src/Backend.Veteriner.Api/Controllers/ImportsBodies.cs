namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// POST /imports/batches form alanları (<c>multipart/form-data</c>). <see cref="RequestId"/> zorunlu; <see cref="Name"/>
/// (≤ 100) ve <see cref="ClinicId"/> (aşı tablosu varsa zorunlu) opsiyonel. Dosya: ya <see cref="File"/> (XLSX) ya da
/// tablo başına CSV alanları; <see cref="Encoding"/>/<see cref="Delimiter"/> CSV algılamasını zorlar.
/// </summary>
/// <summary>POST .../commit gövdesi (IMPORT_API_CONTRACT.md §7.9); <see cref="OnErrors"/> verilmezse <c>SkipErrorRows</c>.</summary>
public sealed class ImportCommitBody
{
    public Guid ValidationId { get; init; }
    public string? OnErrors { get; init; }
}

/// <summary>POST .../release-reminders gövdesi (IMPORT_API_CONTRACT.md §7.10).</summary>
public sealed class ImportReleaseRemindersBody
{
    public int? ConfirmEmailCount { get; init; }
}

public sealed class CreateImportBatchForm
{
    public Guid? RequestId { get; init; }
    public string? Name { get; init; }
    public Guid? ClinicId { get; init; }
    public IFormFile? File { get; init; }
    public IFormFile? CustomersFile { get; init; }
    public IFormFile? PetsFile { get; init; }
    public IFormFile? VaccinationsFile { get; init; }
    public string? Encoding { get; init; }
    public string? Delimiter { get; init; }
}
