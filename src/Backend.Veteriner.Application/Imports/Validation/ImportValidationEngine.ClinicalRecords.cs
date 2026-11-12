using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    /// <summary>İçerik alanları ve sınırları (sıra mükerrer anahtarının da sırasıdır); sınırlar varlıktadır.</summary>
    private static readonly (string Field, int MaxLength)[] ClinicalContentFields =
    [
        ("Sikayet", ImportedClinicalRecord.MaxComplaintLength),
        ("Anamnez", ImportedClinicalRecord.MaxHistoryLength),
        ("Bulgular", ImportedClinicalRecord.MaxFindingsLength),
        ("Tani", ImportedClinicalRecord.MaxDiagnosisLength),
        ("Tedavi", ImportedClinicalRecord.MaxTreatmentLength),
        ("Recete", ImportedClinicalRecord.MaxPrescriptionLength),
        ("LabSonuc", ImportedClinicalRecord.MaxLabResultsLength),
        ("Not", ImportedClinicalRecord.MaxNotesLength),
    ];

    private readonly HashSet<(string PetKey, DateOnly Date, string Content)> _fileClinicalRecords = new();
    private readonly HashSet<(Guid PetId, DateOnly Date, string Content)> _dbClinicalRecords = new();

    /// <summary>Mükerrer kayıt anahtarı: içerik alanlarının katlanmış birleşimi (yazım/boşluk farkı yok sayılır).</summary>
    internal static string ClinicalContentKey(IEnumerable<string?> parts)
        => string.Join('|', parts.Select(p => ImportTextFolding.Fold(p)));

    private ImportRowResult ProcessClinicalRecord(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();
        var (pet, petSkippedByUser) = ResolvePetLink(reader, issues);

        var recordedOn = ReadPastDate(reader, "Tarih", issues);
        if (recordedOn is { } date && pet?.BirthDate is { } birth && date < birth)
            issues.Add(ImportRules.RecordBeforeBirth.At("Tarih", value: reader.Raw("Tarih")));

        var recordType = ReadText(reader, "KayitTuru", ImportedClinicalRecord.MaxRecordTypeLength, issues);
        var content = ClinicalContentFields.Select(f => ReadText(reader, f.Field, f.MaxLength, issues)).ToArray();

        var veterinarian = ReadText(reader, "Hekim", ImportedClinicalRecord.MaxVeterinarianNameLength, issues);

        var rawWeight = reader.Raw("Kilo");
        var (weight, weightValid) = ImportValueParsers.ParseWeight(rawWeight);
        if (!weightValid)
            issues.Add(ImportRules.WeightInvalid.At("Kilo", value: rawWeight));

        var rawTemperature = reader.Raw("Ates");
        var (temperature, temperatureValid) = ImportValueParsers.ParseTemperature(rawTemperature);
        if (!temperatureValid)
            issues.Add(ImportRules.TemperatureInvalid.At("Ates", value: rawTemperature));

        // Yalnız ölçüm (kilo, ateş) taşıyan kayıt da içeriktir; kural varlıkta (ImportedClinicalRecord.Content.HasContent).
        var recordContent = new ImportedClinicalRecord.Content(
            recordType, content[0], content[1], content[2], content[3], content[4], content[5], content[6], content[7],
            veterinarian, weight, temperature);
        var normalized = new ImportNormalizedClinicalRecord(
            pet?.Key, pet?.Owner.Key, recordedOn, recordType,
            content[0], content[1], content[2], content[3], content[4], content[5], content[6], content[7],
            veterinarian, weight, temperature);

        // Boş kayıt taşınacak bilgi içermez: hata değil, atlanır (eski programda doldurulmamış muayene). Diğer sorunları
        // (hasta bulunamadı, tarih) bilgi taşımayan satırda anlamsızdır; kliniği hata listesiyle uğraştırmamak için düşülür.
        if (!recordContent.HasContent())
        {
            issues.Clear();
            issues.Add(ImportRules.ClinicalRecordEmpty.At());
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        if (HasError(issues))
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized);

        if (petSkippedByUser)
        {
            issues.Add(ImportRules.ParentSkipped.At());
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        // Mükerrer: aynı hasta + aynı tarih + aynı içerik (dosyada veya daha önce içe aktarılmış).
        var contentKey = ClinicalContentKey(content);
        var existsInDb = pet!.PetId is { } petId && _dbClinicalRecords.Contains((petId, recordedOn!.Value, contentKey));
        if (existsInDb || !_fileClinicalRecords.Add((pet.Key, recordedOn!.Value, contentKey)))
        {
            issues.Add(ImportRules.DuplicateClinicalRecord.At());
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized, ImportRowAction.Create);
    }

    /// <summary>
    /// Zorunlu geçmiş tarih (klinik geçmiş, hesap hareketi): boş → <c>RequiredMissing</c>, gelecekte → <c>DateInFuture</c>,
    /// 1990 öncesi → <c>DateOutOfRange</c>; hatada null.
    /// </summary>
    private DateOnly? ReadPastDate(ImportRowReader reader, string field, List<ImportIssue> issues)
    {
        var raw = reader.Raw(field);
        if (raw is null)
        {
            issues.Add(ImportRules.RequiredMissing.At(field));
            return null;
        }

        var date = ReadDate(reader, field, issues).Value;
        if (date is not { } value)
            return null;

        if (value > _today)
        {
            issues.Add(ImportRules.DateInFuture.At(field, value: raw));
            return null;
        }

        if (value < ImportValueParsers.MinVaccinationDate)
        {
            issues.Add(ImportRules.DateOutOfRange.At(field, value: raw));
            return null;
        }

        return value;
    }

    /// <summary>Serbest metin alanı (boşluk kırpılır); sınır aşımı <c>FieldTooLong</c>.</summary>
    private static string? ReadText(ImportRowReader reader, string field, int maxLength, List<ImportIssue> issues)
    {
        var value = reader.Raw(field);
        if (value is { Length: var length } && length > maxLength)
            issues.Add(ImportRules.FieldTooLong.At(field, $"{field} en fazla {maxLength} karakter olabilir."));
        return value;
    }
}
