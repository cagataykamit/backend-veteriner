using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    private const int MaxVaccinationNotesLength = 4000;

    private readonly HashSet<(string PetKey, Guid DefinitionId, DateOnly Date)> _fileVaccinations = new();

    /// <summary>HayvanNo bağlantısı yalnız hasta tablosu varsa ve orada HayvanNo eşlenmişse geçerlidir.</summary>
    private bool PetReferenceUsable
        => _input.Tables.TryGetValue(ImportTable.Pets, out var rows) && rows.Count > 0
           && _input.Mapping.ColumnsOf(ImportTable.Pets).ContainsKey("HayvanNo");

    /// <summary>"Diğer" türünün katalog kodu: eski programda türü seçilmemiş hasta (E-vet "Hasta Türü" boş bırakılınca Diğer yazar).</summary>
    private const string UnknownSpeciesCode = "OTHER";

    private HashSet<Guid>? _unknownSpeciesIds;

    /// <summary>Hastanın türü dosyada belirtilmemiş sayılır ("Diğer"): aşı-tür uyumu denetlenmez (kural tek yer).</summary>
    private bool IsUnknownSpecies(Guid speciesId)
        => (_unknownSpeciesIds ??= _input.Catalogs.Species
                .Where(s => string.Equals(s.Code, UnknownSpeciesCode, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Id)
                .ToHashSet())
            .Contains(speciesId);

    private Dictionary<string, Guid?>? _speciesByVaccines;

    /// <summary>
    /// Türü bilinmeyen hastanın (dosyada boş ya da "Diğer") aşılarından çıkan türü: hasta no'suyla bağlanan aşılardan türe özgü
    /// olanların hepsi aynı türü gösteriyorsa o tür; farklı türler ya da türe özgü aşı yoksa null (tür bilinmiyor kalır). Hasta
    /// tablosu aşılardan önce işlendiği için aşı satırları bir kez önden taranır.
    /// </summary>
    private Guid? SpeciesFromVaccines(string? reference)
    {
        if (reference is null)
            return null;

        _speciesByVaccines ??= ScanVaccineSpecies();
        return _speciesByVaccines.GetValueOrDefault(reference);
    }

    private Dictionary<string, Guid?> ScanVaccineSpecies()
    {
        var species = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        if (!PetReferenceUsable || !_input.Tables.TryGetValue(ImportTable.Vaccinations, out var rows))
            return [];

        var columns = _input.Mapping.ColumnsOf(ImportTable.Vaccinations);
        foreach (var row in rows)
        {
            var reader = new ImportRowReader(columns, row);
            var reference = ImportValueParsers.NormalizeText(reader.Raw("HayvanNo"));
            if (reference is null || VaccineSpeciesOf(reader.Raw("AsiAdi")) is not { } vaccineSpecies)
                continue;

            if (_petChoices.TryGetValue(reference, out var choice))
            {
                if (choice.PetNo is null)
                    continue;
                reference = choice.PetNo;
            }

            reference = _mergedPets.GetValueOrDefault(reference) ?? reference;
            if (!species.TryGetValue(reference, out var set))
                species[reference] = set = [];
            set.Add(vaccineSpecies);
        }

        return species.ToDictionary(kv => kv.Key, kv => kv.Value.Count == 1 ? kv.Value.First() : (Guid?)null, StringComparer.Ordinal);
    }

    /// <summary>Aşının türü: eşlenen tanımın türü, yoksa adında geçen tür (<c>KÖPEK KARMA 1</c>); türe özgü değilse null.</summary>
    private Guid? VaccineSpeciesOf(string? rawVaccine)
    {
        var vaccine = _resolver.Resolve(ImportValueKind.Vaccine, rawVaccine);
        if (vaccine.Kind == ImportValueResolutionKind.Resolved && _vaccinesById[vaccine.TargetId!.Value].ParentId is { } species)
            return species;

        return ImportValueResolver.VaccineSpecies(rawVaccine) is { } named
               && _resolver.Resolve(ImportValueKind.Species, named) is { Kind: ImportValueResolutionKind.Resolved, TargetId: { } id }
            ? id
            : null;
    }

    private ImportRowResult ProcessVaccination(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();

        var (pet, petSkip) = ResolvePetLink(reader, issues);

        // Aşı tanımı (hastanın türüne göre; türü bilinmeyen hastada tür süzmeden)
        Guid? definitionId = null;
        string? vaccineName = null;
        var vaccineSkipped = false;
        var speciesUnverified = false;
        var unknownSpecies = pet is not null && IsUnknownSpecies(pet.SpeciesId);
        var rawVaccine = reader.Raw("AsiAdi");
        var vaccine = _resolver.Resolve(ImportValueKind.Vaccine, rawVaccine, unknownSpecies ? null : pet?.SpeciesId);
        switch (vaccine.Kind)
        {
            case ImportValueResolutionKind.Resolved:
                var entry = _vaccinesById[vaccine.TargetId!.Value];
                if (pet is not null && entry.ParentId is { } vaccineSpecies && vaccineSpecies != pet.SpeciesId)
                {
                    // Türü dosyada açıkça yazılı hastada uyumsuz aşı hatadır; türü bilinmeyen hastada aşı reddedilmez, uyarıyla gelir.
                    if (!unknownSpecies)
                    {
                        issues.Add(ImportRules.VaccineInvalidSpecies.At("AsiAdi", value: rawVaccine));
                        break;
                    }

                    issues.Add(ImportRules.VaccineSpeciesUnverified.At("AsiAdi", value: rawVaccine));
                    speciesUnverified = true;
                }

                definitionId = entry.Id;
                vaccineName = entry.Name;
                break;
            case ImportValueResolutionKind.Skipped:
                vaccineSkipped = true;
                break;
            case ImportValueResolutionKind.Unmapped:
                issues.Add(ImportRules.VaccineUnmapped.At("AsiAdi", value: rawVaccine));
                break;
            default:
                issues.Add(ImportRules.RequiredMissing.At("AsiAdi"));
                break;
        }

        // Tarihler
        var rawApplied = reader.Raw("UygulamaTarihi");
        DateOnly? appliedAt = null;
        if (rawApplied is null)
        {
            issues.Add(ImportRules.RequiredMissing.At("UygulamaTarihi"));
        }
        else
        {
            appliedAt = ReadDate(reader, "UygulamaTarihi", issues).Value;
            if (appliedAt is { } applied)
            {
                if (applied > _today)
                {
                    issues.Add(ImportRules.AppliedAtInFuture.At("UygulamaTarihi", value: rawApplied));
                    appliedAt = null;
                }
                else if (applied < ImportValueParsers.MinVaccinationDate)
                {
                    issues.Add(ImportRules.DateOutOfRange.At("UygulamaTarihi", value: rawApplied));
                    appliedAt = null;
                }
                else if (pet?.BirthDate is { } birth && applied < birth)
                {
                    issues.Add(ImportRules.AppliedBeforeBirth.At("UygulamaTarihi", value: rawApplied));
                }
            }
        }

        var rawDue = reader.Raw("SonrakiAsiTarihi");
        var (dueAt, _) = ReadDate(reader, "SonrakiAsiTarihi", issues);
        DateTime? appliedAtUtc = appliedAt is { } a ? OperationDayBounds.NoonUtcForLocalDate(a) : null;
        DateTime? dueAtUtc = null;
        if (dueAt is { } due)
        {
            dueAtUtc = OperationDayBounds.NoonUtcForLocalDate(due);
            if (due > _today.AddYears(VaccinationDueAtWindow.MaxFutureYears))
            {
                issues.Add(ImportRules.DueTooFarInFuture.At("SonrakiAsiTarihi", value: rawDue));
                dueAtUtc = null;
            }
            else if (appliedAtUtc is not null)
            {
                // Durum/tarih tutarlılığı mevcut kuraldan (Applied dalı); bugün 12:00 "gelecek" sayılmasın diye +1 gün.
                var consistency = VaccinationStatusDateRules.Validate(
                    VaccinationStatus.Applied, appliedAtUtc, dueAtUtc, _input.NowUtc.AddDays(1));
                if (!consistency.IsSuccess)
                {
                    issues.Add(ImportRules.DueNotAfterApplied.At("SonrakiAsiTarihi", value: rawDue));
                    dueAtUtc = null;
                }
            }
        }

        // Not: Seri / Uygulayan / Not birleşimi (aşı varlığında ayrı alan yok)
        var notes = BuildNotes(reader.Raw("Seri"), reader.Raw("Uygulayan"), reader.Raw("Not"));
        if (notes is { Length: > MaxVaccinationNotesLength })
            issues.Add(ImportRules.FieldTooLong.At("Not", $"Not (seri/uygulayan ile birlikte) en fazla {MaxVaccinationNotesLength} karakter olabilir."));

        var normalized = new ImportNormalizedVaccination(
            pet?.Key,
            pet?.Owner.Key,
            pet?.Owner.HasEmail ?? false,
            definitionId,
            vaccineName,
            appliedAt,
            dueAt,
            appliedAtUtc,
            dueAtUtc,
            notes,
            speciesUnverified);

        if (HasError(issues))
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized);

        // Bilinçli atlama: hasta veya aşı değeri kullanıcı tarafından atlandı.
        if (petSkip is { } skip)
        {
            issues.Add(skip);
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        if (vaccineSkipped)
        {
            issues.Add(ImportRules.ValueSkipped.At("AsiAdi", value: rawVaccine));
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        // Mükerrer aşı: aynı hasta + aynı tanım + aynı uygulama günü (dosyada veya kiracıda).
        var key = (pet!.Key, definitionId!.Value, appliedAt!.Value);
        var existsInDb = pet.PetId is { } petId && _dbVaccinations.Contains((petId, key.Item2, key.Item3));
        if (existsInDb || !_fileVaccinations.Add(key))
        {
            issues.Add(ImportRules.DuplicateVaccination.At());
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Create);
    }

    /// <summary>
    /// Hastaya bağlanan satırın (aşı, klinik geçmiş, randevu) hayvanını çözer: <c>HayvanNo</c> doluysa dosyadaki hasta; yoksa sahip + hayvan adı + tür ile
    /// dosyadaki sonra sahibin kiracıdaki hastaları.
    /// </summary>
    private (PetRef? Pet, ImportIssue? Skip) ResolvePetLink(ImportRowReader reader, List<ImportIssue> issues)
    {
        var reference = ImportValueParsers.NormalizeText(reader.Raw("HayvanNo"));
        if (reference is not null && PetReferenceUsable)
        {
            // Kullanıcının belirsiz grup seçimi (bölüm 13.4): seçilen hasta ya da "atla"; tek hasta sayılanlar hedefe yönlenir.
            if (_petChoices.TryGetValue(reference, out var choice))
            {
                if (choice.PetNo is null)
                    return (null, ImportRules.AmbiguousPetSkipped.At("HayvanNo", value: reference));
                reference = choice.PetNo;
            }

            reference = _mergedPets.GetValueOrDefault(reference) ?? reference;

            if (!_petByRef.TryGetValue(reference, out var node))
            {
                issues.Add(ImportRules.PetNotFound.At("HayvanNo", reference.StartsWith(ImportRules.AmbiguousPetReferencePrefix, StringComparison.Ordinal)
                    ? $"Aynı sahipte aynı adlı birden çok hasta var (E-vet {reference[ImportRules.AmbiguousPetReferencePrefix.Length..]}); kaydın hangisine ait olduğu ayırt edilemedi."
                    : $"'{reference}' numaralı hasta dosyada bulunamadı.", reference));
                return (null, null);
            }

            if (node.SkippedByUser)
                return (null, ImportRules.ParentSkipped.At());

            if (node.Ref is null)
            {
                issues.Add(ImportRules.PetRowInvalid.At("HayvanNo", $"Hasta aktarılmadı: {node.Reason} (Hastalar satır {node.Row})", reference));
                return (null, null);
            }

            return (node.Ref, null);
        }

        var owner = FindOwnerByNameAndPhone(reader, issues, ImportRules.PetNotFound);
        if (owner is null)
            return (null, null);

        var petName = ImportValueParsers.NormalizeText(reader.Raw("HayvanAdi"));
        if (petName is null)
        {
            issues.Add(ImportRules.PetNotFound.At("HayvanAdi", "Hasta adı boş; hasta bulunamadı."));
            return (null, null);
        }

        var rawSpecies = reader.Raw("Tur");
        if (rawSpecies is null)
            return (FindPetWithoutSpecies(owner, PetNameKey(petName), petName, issues), null);

        var species = _resolver.Resolve(ImportValueKind.Species, rawSpecies);
        if (species.Kind == ImportValueResolutionKind.Skipped)
            return (null, ImportRules.ParentSkipped.At());

        if (species.Kind != ImportValueResolutionKind.Resolved)
        {
            issues.Add(species.Kind == ImportValueResolutionKind.Unmapped
                ? ImportRules.SpeciesUnmapped.At("Tur", value: rawSpecies)
                : ImportRules.PetNotFound.At("Tur", "Hayvan türü boş; hasta bulunamadı."));
            return (null, null);
        }

        var speciesId = species.TargetId!.Value;
        var nameKey = PetNameKey(petName);
        if (_petByIdentity.TryGetValue((owner.Key, nameKey, speciesId), out var filePet))
            return (filePet.Ref, null);

        if (owner.ClientId is { } clientId && _dbPetByIdentity.TryGetValue((clientId, nameKey, speciesId), out var dbPet))
            return (new PetRef($"db:{dbPet.Id}", dbPet.Id, dbPet.SpeciesId, dbPet.BirthDate, owner), null);

        issues.Add(ImportRules.PetNotFound.At("HayvanAdi", "Hasta dosyada veya sahibinin kayıtlı hastaları arasında bulunamadı.", petName));
        return (null, null);
    }

    /// <summary>
    /// Tür boşsa sahibin aynı adlı <b>tek</b> hastası (dosyada, yoksa kiracıda) bağlanır; aynı adlı birden çok hasta (farklı tür)
    /// belirsizdir (<c>PetNotFound</c>). Dosyada bilinçli atlanan hasta aranmaz.
    /// </summary>
    private PetRef? FindPetWithoutSpecies(OwnerRef owner, string nameKey, string petName, List<ImportIssue> issues)
    {
        var filePets = _petsByOwnerName.GetValueOrDefault((owner.Key, nameKey))?.Where(n => n.Ref is not null).ToList() ?? [];
        if (filePets.Count == 1)
            return filePets[0].Ref;

        if (filePets.Count == 0 && owner.ClientId is { } clientId
            && _dbPetsByOwnerName.TryGetValue((clientId, nameKey), out var dbPets) && dbPets.Count == 1)
        {
            var dbPet = dbPets[0];
            return new PetRef($"db:{dbPet.Id}", dbPet.Id, dbPet.SpeciesId, dbPet.BirthDate, owner);
        }

        issues.Add(ImportRules.PetNotFound.At(
            "HayvanAdi",
            filePets.Count > 1 || _dbPetsByOwnerName.GetValueOrDefault((owner.ClientId ?? Guid.Empty, nameKey))?.Count > 1
                ? "Tür olmadığı için hasta ayırt edilemedi (sahibin aynı adlı birden fazla hastası var)."
                : "Hasta dosyada veya sahibinin kayıtlı hastaları arasında bulunamadı.",
            petName));
        return null;
    }

    /// <summary><c>Seri: …; Uygulayan: …; &lt;Not&gt;</c> (dolu olanlar); hepsi boşsa null.</summary>
    private static string? BuildNotes(string? serial, string? administeredBy, string? note)
    {
        var parts = new List<string>();
        if (serial is not null)
            parts.Add($"Seri: {serial}");
        if (administeredBy is not null)
            parts.Add($"Uygulayan: {administeredBy}");
        if (note is not null)
            parts.Add(note);

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
