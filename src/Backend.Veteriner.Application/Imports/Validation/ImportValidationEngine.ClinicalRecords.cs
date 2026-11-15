using System.Globalization;
using Backend.Veteriner.Application.Common.Time;
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

    /// <summary>Geliş kaydı olacak boş muayene satırları (sonuç sırası, hasta, sahip, gün); karar bütün tablolardan sonra.</summary>
    private readonly List<(int Index, string PetKey, string OwnerKey, Guid? PetId, DateOnly Day)> _pendingArrivals = new();

    /// <summary>
    /// Boş muayene satırının gelişi aynı gün başka bir kayıtla zaten görünüyorsa atlanır (kural tek yerde): aynı hastanın aktarılacak
    /// aşısı, dolu klinik kaydı ya da randevusu, sahibinin hesap hareketi (satış, tahsilat), daha önce içe aktarılmış klinik kaydı
    /// ya da aşısı, ya da aynı gün için önceki geliş satırı. Kalanlar geliş kaydı olarak aktarılır.
    /// </summary>
    private void ResolveArrivals()
    {
        if (_pendingArrivals.Count == 0)
            return;

        var pending = _pendingArrivals.Select(a => a.Index).ToHashSet();
        var days = new HashSet<(string Key, DateOnly Day)>();
        for (var i = 0; i < _results.Count; i++)
        {
            var result = _results[i];
            if (pending.Contains(i) || result.Status == ImportRowValidationStatus.Error
                || result.Normalized.Action is not (ImportRowAction.Create or ImportRowAction.Match))
            {
                continue;
            }

            switch (result.Normalized)
            {
                case ImportNormalizedVaccination { PetKey: { } pet, AppliedAt: { } applied }:
                    days.Add(("pet:" + pet, applied));
                    break;
                case ImportNormalizedClinicalRecord { PetKey: { } pet, RecordedOn: { } recorded }:
                    days.Add(("pet:" + pet, recorded));
                    break;
                case ImportNormalizedAppointment { PetKey: { } pet, ScheduledAtUtc: { } scheduled }:
                    days.Add(("pet:" + pet, OperationDayBounds.ToLocalDate(scheduled)));
                    break;
                case ImportNormalizedAccountEntry { OwnerKey: { } owner, OccurredOn: { } occurred }:
                    days.Add(("owner:" + owner, occurred));
                    break;
            }
        }

        var dbDays = _dbClinicalRecords.Select(r => (r.PetId, r.Date)).Concat(_dbVaccinations.Select(v => (v.PetId, v.Date))).ToHashSet();
        foreach (var (index, petKey, ownerKey, petId, day) in _pendingArrivals)
        {
            if (days.Contains(("pet:" + petKey, day)) || days.Contains(("owner:" + ownerKey, day))
                || (petId is { } id && dbDays.Contains((id, day))))
            {
                var row = _results[index];
                _results[index] = Finish(ImportTable.ClinicalRecords, row.RowNumber, [ImportRules.BlankExaminationSameDay.At()], row.Normalized,
                    ImportRowAction.Skip);
                continue;
            }

            // İlk geliş satırı o günü kapsar; aynı gün sonraki boş satırlar atlanır.
            days.Add(("pet:" + petKey, day));
        }
    }

    /// <summary>
    /// Mükerrer kayıt anahtarı: içerik alanları ve hekim (katlanmış; yazım/boşluk farkı yok sayılır), kilo ve ateş (saklanan
    /// hassasiyetle; kilo 0 ölçüm değildir). Yalnız kilo taşıyan iki kayıt kilo farklıysa ayrı kayıttır.
    /// </summary>
    internal static string ClinicalRecordKey(IEnumerable<string?> texts, string? veterinarian, decimal? weightKg, decimal? temperatureC)
        => string.Join('|', texts.Append(veterinarian).Select(p => ImportTextFolding.Fold(p))
            .Append(weightKg > 0 ? Math.Round(weightKg.Value, 2).ToString("0.##", CultureInfo.InvariantCulture) : string.Empty)
            .Append(temperatureC is { } t ? Math.Round(t, 1).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty));

    private ImportRowResult ProcessClinicalRecord(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();
        var (pet, petSkip) = ResolvePetLink(reader, issues);

        var recordedOn = ReadPastDate(reader, "Tarih", issues);
        if (recordedOn is { } date && pet?.BirthDate is { } birth && date < birth)
            issues.Add(ImportRules.RecordBeforeBirth.At("Tarih", value: reader.Raw("Tarih")));

        var recordType = ReadText(reader, "KayitTuru", ImportedClinicalRecord.MaxRecordTypeLength, issues);
        var content = ClinicalContentFields.Select(f => ReadText(reader, f.Field, f.MaxLength, issues)).ToArray();

        var veterinarian = ReadText(reader, "Hekim", ImportedClinicalRecord.MaxVeterinarianNameLength, issues);

        var dropped = new List<string>();
        var rawWeight = reader.Raw("Kilo");
        var (weight, weightValid) = ImportValueParsers.ParseWeight(rawWeight);
        if (!weightValid)
            issues.Add(Dropped(ImportRules.WeightInvalid, "Kilo", "kilo", rawWeight, "geçersiz", dropped, noteOf: "kaydın"));

        var rawTemperature = reader.Raw("Ates");
        var (temperature, temperatureValid) = ImportValueParsers.ParseTemperature(rawTemperature);
        if (!temperatureValid)
            issues.Add(Dropped(ImportRules.TemperatureInvalid, "Ates", "ateş", rawTemperature, "geçersiz", dropped, noteOf: "kaydın"));

        // Alınmayan ölçüm kaybolmaz: kaydın notuna yazılır (içerik sayılır).
        if (dropped.Count > 0)
            content[^1] = string.Join('\n', new[] { content[^1] }.Concat(dropped).Where(s => s is not null));

        // Yalnız ölçüm (kilo, ateş) taşıyan kayıt da içeriktir; kural varlıkta (ImportedClinicalRecord.Content.HasContent).
        var recordContent = new ImportedClinicalRecord.Content(
            recordType, content[0], content[1], content[2], content[3], content[4], content[5], content[6], content[7],
            veterinarian, weight, temperature);
        var normalized = new ImportNormalizedClinicalRecord(
            pet?.Key, pet?.Owner.Key, recordedOn, recordType,
            content[0], content[1], content[2], content[3], content[4], content[5], content[6], content[7],
            veterinarian, weight, temperature);

        if (!recordContent.HasContent())
        {
            // Boş muayene satırı (eski programda doldurulmamış muayene) hastanın o gün geldiğini söyler: hasta ve tarih okunduysa geliş
            // kaydı olur; aynı gün başka kaydı varsa doğrulama sonunda atlanır (ResolveArrivals).
            if (!HasError(issues) && petSkip is null && pet is not null && recordedOn is { } day)
            {
                var arrival = normalized with { RecordType = ImportedClinicalRecord.ArrivalRecordType };
                if (pet.PetId is { } existingPetId && _dbClinicalRecords.Contains((existingPetId, day, ClinicalRecordKey(content, veterinarian, null, null))))
                {
                    issues.Add(ImportRules.ClinicalRecordImportedBefore.At());
                    return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, arrival, ImportRowAction.Skip);
                }

                issues.Add(ImportRules.ClinicalRecordArrival.At());
                _pendingArrivals.Add((_results.Count, pet.Key, pet.Owner.Key, pet.PetId, day));
                return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, arrival, ImportRowAction.Create);
            }

            // Hastası ya da tarihi okunamayan boş satır taşınacak bilgi içermez: hata değil, atlanır. Diğer sorunları (hasta
            // bulunamadı, tarih) bilgi taşımayan satırda anlamsızdır; kliniği hata listesiyle uğraştırmamak için düşülür.
            issues.Clear();
            issues.Add(ImportRules.ClinicalRecordEmpty.At());
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        if (HasError(issues))
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized);

        if (petSkip is { } skip)
        {
            issues.Add(skip);
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        // Mükerrer: aynı hasta + aynı tarih + aynı içerik, hekim ve ölçüm; neden ayrı: daha önce içe aktarılmış ya da dosyada tekrar.
        var contentKey = ClinicalRecordKey(content, veterinarian, weight, temperature);
        if (pet!.PetId is { } petId && _dbClinicalRecords.Contains((petId, recordedOn!.Value, contentKey)))
        {
            issues.Add(ImportRules.ClinicalRecordImportedBefore.At());
            return Finish(ImportTable.ClinicalRecords, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        if (!_fileClinicalRecords.Add((pet.Key, recordedOn!.Value, contentKey)))
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
