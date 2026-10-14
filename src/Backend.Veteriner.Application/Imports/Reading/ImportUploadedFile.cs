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
    ImportUploadedFile? VaccinationsCsv)
{
    public bool HasAny => Workbook is not null || CustomersCsv is not null || PetsCsv is not null || VaccinationsCsv is not null;
    public bool HasCsv => CustomersCsv is not null || PetsCsv is not null || VaccinationsCsv is not null;
}
